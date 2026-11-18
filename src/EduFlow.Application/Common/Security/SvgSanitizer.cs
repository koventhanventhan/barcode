using System.Xml;
using System.Xml.Linq;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Common.Security;

public static class SvgSanitizer
{
    private static readonly HashSet<string> BlockedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "foreignObject",
        "iframe",
        "object",
        "embed",
        "applet",
        "meta",
        "link"
    };

    public static MemoryStream ValidateAndSanitize(MemoryStream input)
    {
        input.Position = 0;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(input, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new DomainValidationException("INVALID_ASSET_TYPE", "Upload a valid SVG image.");
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("INVALID_ASSET_TYPE", "Upload a valid SVG image.");
        }

        foreach (var element in document.Descendants())
        {
            if (BlockedElements.Contains(element.Name.LocalName))
            {
                throw new DomainValidationException("INVALID_ASSET_TYPE", "SVG contains disallowed elements.");
            }

            foreach (var attr in element.Attributes())
            {
                var name = attr.Name.LocalName;
                var value = attr.Value.Trim();

                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DomainValidationException("INVALID_ASSET_TYPE", "SVG contains disallowed event handlers.");
                }

                if (name.Equals("href", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasUnsafeUri(value))
                    {
                        throw new DomainValidationException("INVALID_ASSET_TYPE", "SVG contains disallowed external or script references.");
                    }
                }

                if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && HasUnsafeCss(value))
                {
                    throw new DomainValidationException("INVALID_ASSET_TYPE", "SVG style contains disallowed directives.");
                }
            }
        }

        var output = new MemoryStream();
        document.Save(output, SaveOptions.DisableFormatting);
        output.Position = 0;
        return output;
    }

    private static bool HasUnsafeUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith('#'))
        {
            return false;
        }

        return value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("//", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnsafeCss(string value)
    {
        return value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("expression(", StringComparison.OrdinalIgnoreCase)
            || value.Contains("@import", StringComparison.OrdinalIgnoreCase)
            || value.Contains("url(http", StringComparison.OrdinalIgnoreCase);
    }
}
