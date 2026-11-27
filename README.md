# Visual Studio Translator

Reads the documentation and messages in Visual Studio's tooltips in your language. Hover over code, and the
tooltip's English text is replaced by a translation, with a link to switch back to the original. Everything is
translated on your own computer by a local model: no account, no subscription, and no code leaves your machine.

## What it translates

- **Tooltips in the editor**, for every language Visual Studio supports: C#, Visual Basic, F#, C++, TypeScript,
  JavaScript, HTML, CSS and others. Documentation, parameter descriptions and the messages of errors and warnings.
  Code in a tooltip (names, types, signatures) keeps its colours and is never translated.
- **The Error List**: right-click an entry and choose *Translate Message*.
- **Any text**, in the translator window: *View > Other Windows > Translator* (Ctrl+Shift+Alt+T), or select text in
  the editor and choose *Translate Selection* from its context menu.

It translates into English, Russian, German, French, Spanish, Italian, Portuguese (Brazil), Polish, Czech, Turkish,
Chinese (Simplified), Japanese and Korean; by default into the language Visual Studio is shown in. Its own
interface comes in the same languages.

## Requirements

- Windows on x64.
- Visual Studio 2022 version 17.14 or later, or Visual Studio 2026: Community, Professional or Enterprise.
- Disk space and memory for the model, which the extension offers to download the first time it is needed:
  - **Gemma 4 E4B** (about 5.3 GB): the better translations, chosen on machines with 16 GB of memory or more;
  - **Gemma 4 E2B** (about 3.4 GB): faster and lighter, chosen on machines with less.

  You can choose the model yourself under *Tools > Options > Visual Studio Translator*.
- A graphics card is used when there is one (AMD, Intel or NVIDIA, built-in ones included, through Vulkan); without
  one the model runs on the processor, more slowly.

## Privacy

Translation happens on your computer. The extension connects to the internet only to download, once and after you
agree, the [llama.cpp](https://github.com/ggml-org/llama.cpp) runtime from GitHub and the model from Hugging Face.
Translations are cached on your disk so that the same text is not translated twice. The logs it keeps hold states
and reasons for errors, never the text being translated.

## Settings

*Tools > Options > Visual Studio Translator*:

| Setting | Default |
|---|---|
| Translate tooltips | On |
| Translate into | Same as Visual Studio |
| Translate error messages | On |
| Replace the original with the translation (when off, the translation is shown below the original) | On |
| Language model | Automatic, by the computer's memory |
| Use the graphics card | On |

## Building from source

You need the .NET 10 SDK on Windows.

```powershell
dotnet build
dotnet test
```

A Release build (`dotnet build -c Release`) also publishes the Engine into the `.vsix`, which is then ready to install.
In Visual Studio, F5 on `VisualStudioTranslator.Vsix` starts the experimental instance with the extension.

## License

[MIT](LICENSE). The components the extension ships or downloads are listed, with their licenses, in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).