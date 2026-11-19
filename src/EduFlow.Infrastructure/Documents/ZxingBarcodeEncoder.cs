using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using EduFlow.Application.Academics;

namespace EduFlow.Infrastructure.Documents;

public sealed class ZxingBarcodeEncoder : IBarcodeEncoder, IQrCodeEncoder
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

    public QrMatrixDto EncodeQr(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("QR content is required.", nameof(content));
        }

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 2 };
        var matrix = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, 0, 0, hints);
        var size = matrix.Width;
        var chars = new char[size * size];
        for (var y = 0; y < size; y++)
        {
            var offset = y * size;
            for (var x = 0; x < size; x++)
            {
                chars[offset + x] = matrix[x, y] ? '1' : '0';
            }
        }

        return new QrMatrixDto(size, new string(chars));
    }
}
