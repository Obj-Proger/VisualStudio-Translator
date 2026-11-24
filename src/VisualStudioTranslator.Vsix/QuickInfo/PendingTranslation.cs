using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The translation section of a tooltip that opened before its translation was ready. It starts out
/// saying the translation is on its way and is replaced in place when it arrives, which works because the
/// tooltip keeps showing the very element it was given.
/// <para>
/// Both states are drawn by Visual Studio's own view element factory from the same elements a ready
/// translation uses, so the section looks the same whichever way it came to be.
/// </para>
/// </summary>
internal sealed class PendingTranslation
{
    private readonly IViewElementFactoryService _viewElements;
    private readonly ITextView _view;
    private readonly ContentControl _host;

    public PendingTranslation(IViewElementFactoryService viewElements, ITextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _viewElements = viewElements;
        _view = view;
        _host = new ContentControl { Focusable = false, Content = Render(DocumentationElements.Pending()) };
    }

    /// <summary>What goes into the tooltip.</summary>
    public UIElement Element => _host;

    /// <summary>Puts the translation in place, or takes the section away when there is none to show.</summary>
    public void Show(ContainerElement? translation)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (translation is null)
        {
            _host.Content = null;
            _host.Visibility = Visibility.Collapsed;
            return;
        }

        _host.Content = Render(translation);
    }

    private UIElement? Render(ContainerElement element) => _viewElements.CreateViewElement<UIElement>(_view, element);
}