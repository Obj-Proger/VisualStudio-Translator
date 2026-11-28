# Using Visual Studio Translator

## Tooltips

Hover over code. While the translation is on its way, a thin strip slides under the tooltip; then the tooltip's text
is replaced by the translation, and a *Show original* link switches between the two. Where the extension cannot
match the tooltip's lines exactly, it shows the translation below the original instead.

Text already in the language you translate into is left as it is, and a text translated once comes from a cache on
your disk the next time.

## Signature help

While you type a method's arguments, the popup with its signatures shows its documentation translated, in place of
the original; the signatures themselves stay as they are. The popup has no room for a link back to the original, so
there is none, and where the extension cannot match its lines exactly it leaves the popup as it is. It follows the
*Translate tooltips* setting.

## The translator window

*View > Other Windows > Translator*, or Ctrl+Shift+Alt+T. Type or paste text on the left; the translation appears on
the right. In the editor, select text and choose *Translate Selection* from the context menu, or press the same
shortcut. In the Error List, right-click entries and choose *Translate Message*.

## The model

The first time something is to be translated, the extension offers to download the model and the runtime that runs
it. Which model depends on your computer's memory:

- **Gemma 4 E4B**, about 5.3 GB: the better translations, with 16 GB of memory or more;
- **Gemma 4 E2B**, about 3.4 GB: faster and lighter, with less.

The model uses a graphics card when there is one (AMD, Intel or NVIDIA, built-in ones included) and the processor
otherwise.

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

## Logs

When something does not work, the logs say why. They hold states and reasons for errors, never the text being
translated:

- `%LOCALAPPDATA%\VisualStudioTranslator\logs\vsix.log`, the extension's;
- the Engine's, in the same folder.