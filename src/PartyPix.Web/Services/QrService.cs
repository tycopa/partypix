using QRCoder;

namespace PartyPix.Web.Services;

public class QrService
{
    /// <summary>Render a QR code as SVG for embedding on the event detail page.</summary>
    public string RenderSvg(string payload, int pixelsPerModule = 10)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(pixelsPerModule);
    }

    public byte[] RenderPng(string payload, int pixelsPerModule = 20)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
