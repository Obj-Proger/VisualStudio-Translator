# Visual Studio Translator

Reads Visual Studio's tooltips in your language. Hover over code and the tooltip's English text is replaced by a
translation, with a link back to the original. A local model translates on your own computer: no account, no
subscription, no code sent anywhere.

## What it translates

- **Tooltips**, in every language Visual Studio supports: documentation, parameter descriptions, error and warning
  messages. Code in a tooltip keeps its colours and is never translated.
- **Error List messages**: right-click an entry and choose *Translate Message*.
- **Any text**, in the translator window: *View > Other Windows > Translator* (Ctrl+Shift+Alt+T).

Into English, Russian, German, French, Spanish, Italian, Portuguese (Brazil), Polish, Czech, Turkish, Chinese
(Simplified), Japanese or Korean, by default the language Visual Studio is shown in.

## Requirements

- Windows x64 with Visual Studio 2022 17.14 or later, or Visual Studio 2026.
- 3.4 to 5.3 GB of disk space for the model, which the extension offers to download the first time it is needed.
  A graphics card speeds it up; without one it runs on the processor.

## Privacy

Translation happens on your computer. The extension goes online only to download, once and after you agree, the
[llama.cpp](https://github.com/ggml-org/llama.cpp) runtime and the model.

## More

- [Using the extension](docs/usage.md): what it does where, and its settings.
- [Contributing](CONTRIBUTING.md): building from source and sending changes.

## License

[GNU GPL version 3 only](LICENSE), with an additional permission to combine it with Visual Studio: see
[NOTICE.md](NOTICE.md).