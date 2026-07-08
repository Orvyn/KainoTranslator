# KainoTranslator

[English](README.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Deutsch](README.de.md) | **Polski**

Lekka aplikacja desktopowa dla Windows, która rozpoznaje tekst (OCR) w wybranym prostokątnym
obszarze ekranu i wyświetla nad nim tłumaczenie na żywo — stworzona z myślą o napisach w grach,
oknach dialogowych, napisach w filmach oraz dowolnym obcojęzycznym tekście na ekranie.

- Wybór obszaru przechwytywania, włączanie/wyłączanie tłumaczenia na żywo i otwieranie ustawień
  — wszystko za pomocą skrótów klawiszowych.
- Rozpoznaje tekst za pomocą **Windows OCR** (zalecana opcja domyślna, wbudowana w Windows, bez
  konfiguracji) lub, jeśli wolisz, starszych silników **Tesseract** / **EasyOCR**.
- Tłumaczy za pomocą **DeepL**, **Google Translate**, **Yandex Translate** lub **Papago**.
- Solidne wsparcie dla języka angielskiego, japońskiego, chińskiego (uproszczonego i
  tradycyjnego), koreańskiego i rosyjskiego, a także ok. 25 dodatkowych języków (francuski,
  niemiecki, hiszpański, włoski, portugalski, niderlandzki, polski, turecki, wietnamski, tajski,
  indonezyjski, arabski, hindi, ukraiński i większość pozostałych języków europejskich — pełna
  lista w `Models/LanguageCatalog.cs`).
- Opcjonalna lista serwerów proxy do rotacji zapytań, głównie po to, by darmowy punkt końcowy
  Google nie był blokowany po adresie IP przy intensywnym użytkowaniu.
- Nakładka przezroczysta dla kliknięć, przyjazna trybowi bez ramki, zaprojektowana tak, by
  wyświetlać się nad grami bez przechwytywania fokusu ani wejścia.

> **Uwaga:** obecnie interfejs samej aplikacji jest wyłącznie w języku rosyjskim. Nazwy
> zakładek/menu podane są poniżej tak, jak faktycznie wyglądają w aplikacji (po rosyjsku), z
> polskim tłumaczeniem w nawiasie przy pierwszym wystąpieniu.

## Wymagania

- Windows 10 (aktualizacja 2018 / 1809) lub Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) do zbudowania aplikacji.
- Najprościej otworzyć/zbudować/uruchomić w Visual Studio 2022 (17.8+), ale .NET CLI również
  zadziała.

## Budowanie i uruchamianie

```
cd KainoTranslator
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

Albo otwórz `ScreenTranslator.sln` w Visual Studio i naciśnij F5. Aplikacja nie ma głównego
okna — działa z **zasobnika systemowego** (prawy dolny róg paska zadań). Kliknij prawym
przyciskiem myszy na ikonę, aby otworzyć menu, lub użyj skrótów klawiszowych poniżej.

Brak okna po uruchomieniu jest zamierzony; jeśli nie widzisz ikony w zasobniku, sprawdź strzałkę
„Pokaż ukryte ikony" na pasku zadań.

## Domyślne skróty klawiszowe

| Czynność                                      | Domyślna kombinacja |
|--------------------------------------------------|-----------------|
| Wybierz obszar przechwytywania                     | `Ctrl+Shift+A`  |
| Włącz / wyłącz tłumaczenie na żywo                  | `Ctrl+Shift+S`  |
| Przetłumacz obszar jednorazowo                       | `Ctrl+Shift+D`  |
| Otwórz ustawienia                                   | `Ctrl+Shift+O`  |

Wszystkie cztery można zmienić w zakładce **Горячие клавиши** (Skróty klawiszowe) — kliknij pole
i naciśnij żądaną kombinację (musi zawierać co najmniej jeden klawisz modyfikujący: Ctrl, Alt,
Shift lub Win). Jeśli rejestracja skrótu się nie powiedzie (pojawi się dymek powiadomienia w
zasobniku), oznacza to, że ta kombinacja jest już używana przez inną aplikację — wybierz inną.

Skrót „przetłumacz obszar jednorazowo" pozwala wybrać dowolny obszar ekranu i przetłumaczyć go
jednorazowo (np. fragment tekstu, który pojawił się gdzie indziej na ekranie), bez zmiany
głównego obszaru przechwytywania i bez przerywania trwającego tłumaczenia na żywo.

## Pierwsza konfiguracja

1. Uruchom aplikację (pojawi się ikona w zasobniku).
2. Naciśnij `Ctrl+Shift+A` i zaznacz prostokątem obszar z napisami/dialogiem do przetłumaczenia.
3. Naciśnij `Ctrl+Shift+O`, aby otworzyć ustawienia:
   - **Перевод** (Tłumaczenie) — silnik OCR (domyślnie Windows OCR), język źródłowy, translator,
     język docelowy oraz klucz API dla wybranego translatora (patrz niżej).
   - Zapisz.
4. Naciśnij `Ctrl+Shift+S`, aby rozpocząć tłumaczenie na żywo. Przetłumaczony tekst pojawi się w
   nakładce w pobliżu obszaru przechwytywania.

## Silniki OCR

- **Windows OCR (zalecany, domyślny)** — nie wymaga pobierania, ale dla wybranego języka musi
  być zainstalowany odpowiedni komponent OCR: **Ustawienia Windows > Czas i język > Język i
  region > Dodaj język**, a następnie dla tego języka włącz **„Rozpoznawanie znaków"** w jego
  opcjach dodatkowych. Szybki i zazwyczaj najdokładniejszy przy czystym tekście UI/napisach.
- **Tesseract (starszy)** — działa całkowicie offline. Pobierz pliki `.traineddata` dla
  potrzebnych języków z https://github.com/tesseract-ocr/tessdata_fast i umieść je w folderze
  wskazanym w zakładce **Прочее** (Inne) w polu „Folder z plikami językowymi (.traineddata)"
  (domyślnie `.\tessdata` obok pliku exe).
- **EasyOCR (starszy)** — wymaga Pythona 3.9+ w PATH (lub wskazania ścieżki do niego w zakładce
  **Прочее**, Inne) oraz jednorazowego wykonania `pip install easyocr`. Aplikacja uruchamia
  `Python\easyocr_worker.py`, który utrzymuje model EasyOCR w pamięci i odpowiada na jedno
  żądanie rozpoznawania na linię standardowego wejścia — dzięki temu unika się ponownego
  (wolnego) ładowania modelu przy każdej klatce. Akceleracja GPU jest opcjonalna i wymaga
  kompilacji PyTorch z obsługą CUDA.

## Translatory i klucze API

| Translator | Co jest potrzebne | Uwagi |
|---|---|---|
| **DeepL** | Klucz API z deepl.com/pro-api (dostępny darmowy plan) | Włącz „Użyj endpointu Pro" tylko wtedy, gdy masz płatny klucz Pro — darmowe klucze muszą pozostać na darmowym endpoincie. |
| **Google Translate** | Nic nie jest wymagane | Działa od razu przez darmowy endpoint webowy Google. Dodaj klucz Cloud Translation API, jeśli potrzebujesz gwarantowanej dostępności/limitu zamiast darmowego endpointu. |
| **Yandex Translate** | Klucz API + Folder ID z Yandex Cloud | Oba pola są wymagane. |
| **Papago** | Client ID + Client Secret z konsoli NAVER Cloud Platform | Papago obsługuje tylko ograniczony zestaw par językowych, głównie za pośrednictwem koreańskiego/angielskiego/japońskiego/chińskiego — dostępne będą tylko obsługiwane języki docelowe. |

## Serwery proxy

Zakładka **Прочее** (Inne), na samym dole, zawiera listę proxy — jeden serwer na linię:
```
http://1.2.3.4:8080
http://login:haslo@1.2.3.4:8080
1.2.3.4:8080
```
Pozostaw puste, aby łączyć się bezpośrednio (domyślnie). Najbardziej przydatne przy darmowym
endpoincie Google, który nie ma oficjalnej gwarancji limitu zapytań i przy intensywnym,
szybkim odpytywaniu może zacząć odrzucać żądania — rotacja przez kilka serwerów proxy (domyślnie
włączona, gdy lista nie jest pusta) rozkłada obciążenie na różne adresy IP.

## Używanie z grami

- **Uruchamiaj grę w trybie bez ramki lub w trybie okna.** W większości gier można to
  przełączyć skrótem `Alt+Enter` lub w ustawieniach wyświetlania. To najważniejsza wskazówka.
- **Tryb pełnoekranowy wyłączny** może powodować dziwne zachowanie zarówno przechwytywania
  ekranu, jak i nakładki, ponieważ niektóre gry w tym trybie omijają standardowy kompozytor
  Windows (DWM) — przechwycony obraz może wyjść czarny, a nakładka może się nie wyświetlić na
  wierzchu. Tryb bez ramki/okna jest renderowany przez DWM tak jak każde inne okno, więc zarówno
  przechwytywanie, jak i nakładka działają poprawnie. Nowsze wersje Windows/DirectX coraz
  częściej unikają prawdziwego wyłącznego trybu pełnoekranowego, ale jeśli dana gra oferuje
  tylko „Pełny ekran", spróbuj trybu okna/bez ramki, gdy nakładka wyświetla się nieprawidłowo.
- Nakładka jest **domyślnie przezroczysta dla kliknięć** (przełącznik w zakładce
  **Оформление**, Wygląd), więc nigdy nie blokuje kliknięć myszy ani wejścia z klawiatury
  przeznaczonego dla gry pod spodem.
- Aplikacja **uwzględnia DPI każdego monitora osobno**, dzięki czemu obszar przechwytywania i
  pozycja nakładki pozostają dokładne nawet w konfiguracjach wielomonitorowych z różną skalą lub
  po przeciągnięciu gry między monitorami po wybraniu obszaru (wybierz obszar ponownie, jeśli
  zmienisz monitor na taki o innej rozdzielczości/skali).

## Wygląd nakładki

Zakładka **Оформление** (Wygląd) pozwala ustawić:
- Pozycję (domyślnie: górna część ekranu, wyśrodkowana), rozmiar czcionki i maksymalną szerokość
  (domyślnie 1200px).
- Kolor tekstu i kolor tła (hex, np. `#FFFFFF`) z podglądem na żywo oraz przezroczystość tła
  (domyślnie 20%; 0% oznacza tło całkowicie nieprzezroczyste, 100% — całkowicie przezroczyste).
- Albo 1-pikselowy kontur, **albo** cień pod tekstem dla czytelności na dowolnym tle (nie oba
  naraz) — domyślnie wyłączone.
- Opcjonalne automatyczne ukrywanie: ukryj nakładkę automatycznie po N sekundach, jeśli tekst
  przestaje się aktualizować.

## Uwagi dotyczące wydajności

- Interwał odpytywania (domyślnie 0,3 s, zakładka **Перевод**, Tłumaczenie, wyświetlany w
  sekundach) określa, jak często ponownie sprawdzany jest obszar przechwytywania.
- Opcja „Nie rozpoznawaj i nie tłumacz ponownie, jeśli obraz się nie zmienił" (domyślnie
  włączona) haszuje pomniejszoną wersję każdej przechwyconej klatki i całkowicie pomija OCR oraz
  wywołanie API translatora, gdy nic się nie zmieniło — dzięki temu szybki interwał odpytywania
  pozostaje tani zarówno pod względem obciążenia procesora, jak i zużycia API tłumaczenia,
  ponieważ napisy/dialogi zwykle pozostają niezmienione przez sekundę lub dłużej.
- Rozpoznany tekst musi być taki sam (pomijając drobne różnice w spacjach/formatowaniu) lub
  bardzo podobny na dwóch kolejnych sprawdzeniach, zanim zostanie przetłumaczony — jednak tekst,
  który znika już po pierwszym sprawdzeniu (np. krótkie napisy w scenkach przerywnikowych), i
  tak zostanie przetłumaczony, zamiast zostać po prostu odrzucony. Zapobiega to migotaniu/
  zmienianiu się wyświetlanego tłumaczenia z powodu pojedynczego zaszumionego odczytu OCR.
  Efektem ubocznym jest to, że napisy pojawiające się na ekranie stopniowo (efekt maszyny do
  pisania) są tłumaczone dopiero, gdy przestają się zmieniać, czyli gdy są już w pełni
  wyświetlone — to nie jest osobna funkcja, lecz naturalny skutek tego samego sprawdzania
  stabilności.
- Powtórzenie identycznego rozpoznanego tekstu również pomija zbędne wywołanie tłumaczenia.
- Odczuwalne opóźnienie to głównie czas OCR i komunikacji sieciowej z translatorem; Windows OCR
  działa lokalnie i zwykle jest najszybszy spośród trzech silników, więc to najlepszy wybór,
  gdy liczy się zarówno szybkość, jak i prostota.

## Struktura projektu

```
KainoTranslator/
  App.xaml(.cs)              - ikona w zasobniku, obsługa skrótów, ogólna koordynacja
  TranslationEngine.cs        - pętla przechwytywanie -> OCR -> tłumaczenie -> nakładka
  Capture/                    - interfejs wyboru obszaru + przechwytywanie ekranu + haszowanie klatek
  Ocr/                        - IOcrEngine + implementacje Windows OCR / Tesseract / EasyOCR
  Translation/                - ITranslator + implementacje DeepL / Google / Yandex / Papago, menedżer proxy
  Overlay/                    - zawsze widoczne na wierzchu okno nakładki z tłumaczeniem
  Hotkeys/                    - rejestracja globalnych skrótów klawiszowych (RegisterHotKey)
  SettingsUi/                 - okno ustawień
  Models/                     - AppSettings (zapisywane w %AppData%\KainoTranslator\settings.json), LanguageCatalog
  Python/easyocr_worker.py    - skrypt roboczy EasyOCR (potrzebny tylko przy używaniu silnika EasyOCR)
```

## Znane ograniczenia / rozwiązywanie problemów

- Windows OCR wymaga zainstalowania funkcji OCR dla danego języka poprzez ustawienia systemu
  Windows — aplikacja poinformuje o tym wprost, jeśli brakuje tej funkcji, zamiast zawieść po
  cichu.
- Ograniczenia darmowego endpointu Google oraz obsługiwanych par językowych Papago opisano
  powyżej; aplikacja wyświetla czytelne komunikaty o błędach (poprzez dymek w zasobniku)
  zamiast zawodzić po cichu, gdy translator odrzuci żądanie.
- Jeśli skrót klawiszowy nie działa, prawdopodobnie ta sama kombinacja jest już zarejestrowana
  globalnie przez inną aplikację — wybierz inną kombinację w ustawieniach.
