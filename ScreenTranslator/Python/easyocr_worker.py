"""
EasyOCR worker used by ScreenTranslator's "EasyOCR (legacy)" OCR option.

Protocol (simple line-based stdin/stdout so the .NET app doesn't need extra
dependencies to talk to it):
  1. On startup, loads the EasyOCR reader for the requested language(s) and
     prints "READY" once the model is loaded.
  2. Then repeatedly reads a line from stdin containing a path to a PNG file,
     runs OCR on it, and prints one line of JSON: {"lines": ["...", "..."]}
  3. Reading the line "EXIT" shuts the worker down.

Requires: pip install easyocr
"""
import sys
import json
import argparse


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lang", required=True, help="EasyOCR language code, e.g. en, ja, ch_sim, ko, ru")
    parser.add_argument("--gpu", action="store_true", help="Use GPU (requires a CUDA-enabled torch install)")
    args = parser.parse_args()

    try:
        import easyocr
    except ImportError:
        print(json.dumps({"error": "easyocr is not installed. Run: pip install easyocr"}), flush=True)
        sys.exit(1)

    # EasyOCR requires English alongside most East Asian languages in the same reader.
    langs = [args.lang]
    if args.lang not in ("en",) and args.lang not in ("ch_sim", "ch_tra", "ja", "ko"):
        # Latin-script languages combine fine with English; CJK languages must be used alone (EasyOCR limitation).
        langs = [args.lang, "en"]

    reader = easyocr.Reader(langs, gpu=args.gpu, verbose=False)
    print("READY", flush=True)

    for line in sys.stdin:
        path = line.strip()
        if not path:
            continue
        if path == "EXIT":
            break
        try:
            results = reader.readtext(path, detail=0, paragraph=True)
            print(json.dumps({"lines": results}, ensure_ascii=False), flush=True)
        except Exception as e:
            print(json.dumps({"lines": [], "error": str(e)}), flush=True)


if __name__ == "__main__":
    main()
