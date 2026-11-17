using ZXing;
using ZXing.Common;
using EduFlow.Application.Academics;

namespace EduFlow.Infrastructure.Documents;

public sealed class ZxingBarcodeEncoder : IBarcodeEncoder
{
    public bool[] EncodeCode128(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Barcode content is required.", nameof(content));
        }

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 10 };
        var matrix = new ZXing.OneD.Code128Writer().encode(content, BarcodeFormat.CODE_128, 1, 1, hints);
        var modules = new bool[matrix.Width];
        for (var x = 0; x < matrix.Width; x++)
        {
            modules[x] = matrix[x, 0];
        }

        return modules;
    }
}
