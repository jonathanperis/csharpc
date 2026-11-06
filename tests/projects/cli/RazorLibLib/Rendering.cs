// The members of the ASP.NET Core components runtime that Razor-generated components call, with a
// renderer that writes HTML text instead of building a render tree for a browser.
using System.Text;

namespace Microsoft.AspNetCore.Components
{
    public interface IComponent
    {
    }

    public abstract class ComponentBase : IComponent
    {
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder)
        {
        }

        public string RenderHtml()
        {
            var builder = new Rendering.RenderTreeBuilder();
            BuildRenderTree(builder);
            return builder.ToString();
        }
    }
}

namespace Microsoft.AspNetCore.Components.Rendering
{
    public sealed class RenderTreeBuilder
    {
        private readonly StringBuilder _html = new();
        private readonly Stack<string> _open = new();
        private bool _inStartTag;

        public void OpenElement(int sequence, string elementName)
        {
            CloseStartTag();
            _html.Append('<').Append(elementName);
            _open.Push(elementName);
            _inStartTag = true;
        }

        public void CloseElement()
        {
            CloseStartTag();
            _html.Append("</").Append(_open.Pop()).Append('>');
        }

        public void AddAttribute(int sequence, string name, object? value) =>
            _html.Append(' ').Append(name).Append("=\"").Append(Encode(value?.ToString())).Append('"');

        public void AddAttribute(int sequence, string name, string? value) => AddAttribute(sequence, name, (object?)value);

        public void AddMarkupContent(int sequence, string? markupContent)
        {
            CloseStartTag();
            _html.Append(markupContent);
        }

        public void AddContent(int sequence, string? textContent)
        {
            CloseStartTag();
            _html.Append(Encode(textContent));
        }

        public void AddContent(int sequence, object? textContent) => AddContent(sequence, textContent?.ToString());

        public override string ToString() => _html.ToString();

        private void CloseStartTag()
        {
            if (!_inStartTag) return;
            _html.Append('>');
            _inStartTag = false;
        }

        private static string Encode(string? text) =>
            (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
