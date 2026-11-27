# Contributing

Thank you for wanting to improve Visual Studio Translator.

## Before you send a pull request

Every contribution is accepted under the [Contributor License Agreement](CLA.md), which gives the project's author
all rights in it, so that the project can stay under one owner's terms. The first time you open a pull request, a
bot asks you to sign it by posting a comment; one signature covers all your contributions.

For anything larger than a fix, open an issue first, so that we can agree on the approach before you spend time on
it.

## Building

You need Windows and the .NET 10 SDK.

```powershell
dotnet build
dotnet test
```

A Release build (`dotnet build -c Release`) also publishes the Engine into the `.vsix`, which can then be installed.
In Visual Studio, F5 on `VisualStudioTranslator.Vsix` builds the extension, installs it into the experimental instance
and starts it.

The projects:

- `src/VisualStudioTranslator.Core`: shared code (.NET Standard 2.0): documentation model, protocol, languages;
- `src/VisualStudioTranslator.Engine`: the process that translates (.NET 10), reached over a named pipe;
- `src/VisualStudioTranslator.Vsix`: the extension (.NET Framework 4.8);
- `tests/`: tests for Core and the Engine.

## Conventions

- A build with `CI=true` treats warnings as errors, as GitHub Actions does; run it before you send changes.
- Code, comments and commit messages in English; commit messages follow
  [Conventional Commits](https://www.conventionalcommits.org).
- Follow the style the code already has; `.editorconfig` describes it.
- The extension's own words are in `src/VisualStudioTranslator.Vsix/Resources/Strings*.resx`, in every language it
  speaks; a new text needs all of them.