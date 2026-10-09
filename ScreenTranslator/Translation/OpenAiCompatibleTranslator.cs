using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

/// <summary>
/// Talks to any provider that implements the OpenAI chat-completions request/response shape at
/// /chat/completions under a configurable base URL - this covers OpenAI itself, Google's Gemini
/// (generativelanguage.googleapis.com/v1beta/openai/), DeepSeek (api.deepseek.com/v1), Groq, and
/// most locally hosted servers (Ollama, LM Studio), all with this one class. Unlike the other
/// translators here, there's no fixed per-language code table to look up: the target language is
/// just named in the prompt, so every language in LanguageCatalog is usable as a target (see
/// LanguageCatalog.ForTranslator).
/// </summary>
public sealed class OpenAiCompatibleTranslator : IContextualTranslator
{
    public string Name => "OpenAI-совместимый";

    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly ProxyManager _proxies;

    public OpenAiCompatibleTranslator(string baseUrl, string model, string apiKey, ProxyManager proxies)
    {
        _baseUrl = baseUrl;
        _model = model;
        _apiKey = apiKey;
        _proxies = proxies;
    }

    public Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
        => TranslateAsync(text, sourceLanguageKey, targetLanguageKey, null, ct);

    public async Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, TranslationContext? context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("OpenAI-совместимый провайдер не настроен: не указан URL (Настройки > Сервис перевода).");
        if (string.IsNullOrWhiteSpace(_model))
            throw new InvalidOperationException("OpenAI-совместимый провайдер не настроен: не указана модель (Настройки > Сервис перевода).");

        var targetName = LanguageCatalog.ByKey(targetLanguageKey).DisplayName;
        var sourceName = sourceLanguageKey is not (null or "auto") ? LanguageCatalog.TryByKey(sourceLanguageKey)?.DisplayName : null;

        // Kept deliberately strict: a model asked to "translate" tends to add commentary,
        // alternate phrasings, or wrap the result in quotes/explanation unless told not to - any
        // of that would end up shown as if it were the translation itself.
        var systemPrompt = sourceName != null
            ? $"You are a translation engine. Translate the user's text from {sourceName} to {targetName}. " +
              "Reply with ONLY the translation - no quotes, no notes, no alternatives, no explanation."
            : $"You are a translation engine. Detect the user's text's language and translate it to {targetName}. " +
              "Reply with ONLY the translation - no quotes, no notes, no alternatives, no explanation.";

        systemPrompt += BuildContextPrompt(text, context);

        var requestBody = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text },
            },
            temperature = 0.2,
        });

        var url = _baseUrl.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var client = _proxies.GetClient(TimeSpan.FromSeconds(60));
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation; without this it looked like the
            // user stopping the translation and the line was dropped without a word.
            throw new TimeoutException("OpenAI-совместимый провайдер не ответил за 60 секунд.");
        }
        using var responseScope = response;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI-совместимый провайдер вернул ошибку {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0) return "";

        var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return content.Trim();
    }

    /// <summary>
    /// The optional part of the system prompt: what the text is from, which glossary terms apply
    /// and the lines just before it. Only glossary terms that actually occur in the text (or the
    /// previous lines) are listed - sending the whole glossary every time would cost tokens and
    /// tempt the model to mention terms that aren't there.
    /// </summary>
    private static string BuildContextPrompt(string text, TranslationContext? context)
    {
        if (context is null) return "";
        var sb = new StringBuilder();

        if (context.IsCharacterName)
            sb.Append(" The text is the name of a character (or a place) from a video game or story: render it the way an official localization would, and keep it as short as the original.");

        if (!string.IsNullOrWhiteSpace(context.GameContext))
            sb.Append(" Context of the text: ").Append(context.GameContext.Trim()).Append(".");

        if (text.Contains('\n'))
            sb.Append(" Keep the same line breaks as in the original.");

        var haystack = text + "\n" + string.Join("\n", context.PreviousLines);
        var terms = context.Glossary
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceTerm) &&
                        haystack.IndexOf(e.SourceTerm.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        if (terms.Count > 0)
        {
            sb.Append("\nGlossary - follow it strictly. Whenever the left term appears, in any form, use the right side, inflected naturally for the target language. \"(keep)\" means leave the term exactly as written:");
            foreach (var e in terms)
                sb.Append("\n- ").Append(e.SourceTerm.Trim()).Append(" => ")
                  .Append(string.IsNullOrWhiteSpace(e.TargetTerm) ? "(keep)" : e.TargetTerm.Trim());
        }

        if (context.PreviousLines.Count > 0)
        {
            sb.Append("\nPrevious lines of the same dialogue, only for context (do NOT translate or repeat them):");
            foreach (var line in context.PreviousLines)
                sb.Append("\n> ").Append(line.Replace("\n", " "));
        }

        return sb.ToString();
    }
}
