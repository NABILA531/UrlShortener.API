using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace UrlShortener.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QRCodeController : ControllerBase
{
    [HttpGet]
    public IActionResult GenerateQRCode([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest("URL cannot be empty.");
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(
            url,
            QRCodeGenerator.ECCLevel.Q);

        var pngQrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = pngQrCode.GetGraphic(20);

        return File(qrCodeBytes, "image/png");
    }
}
