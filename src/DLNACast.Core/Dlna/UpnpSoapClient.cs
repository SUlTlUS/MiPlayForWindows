using System.Net;
using System.Text;
using System.Xml.Linq;
using DLNACast.Core.Models;

namespace DLNACast.Core.Dlna;

internal sealed class UpnpSoapClient : IDisposable
{
    private static readonly XNamespace SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    private readonly HttpClient _httpClient;

    public UpnpSoapClient(TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(4)
        })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(6)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DLNACast/0.1 UPnP/1.1");
    }

    public HttpClient HttpClient => _httpClient;

    public async Task<XDocument> InvokeAsync(
        UpnpServiceEndpoint service,
        string action,
        IReadOnlyDictionary<string, string?> arguments,
        CancellationToken cancellationToken)
    {
        XNamespace serviceNamespace = service.ServiceType;
        var actionElement = new XElement(serviceNamespace + action,
            new XAttribute(XNamespace.Xmlns + "u", serviceNamespace.NamespaceName),
            arguments.Select(pair => new XElement(pair.Key, pair.Value ?? string.Empty)));
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(SoapNamespace + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", SoapNamespace.NamespaceName),
                new XAttribute(SoapNamespace + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
                new XElement(SoapNamespace + "Body", actionElement)));

        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUrl)
        {
            Content = new StringContent(document.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service.ServiceType}#{action}\"");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        XDocument? responseDocument = null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try { responseDocument = XDocument.Parse(payload, LoadOptions.PreserveWhitespace); }
            catch (Exception ex) when (!response.IsSuccessStatusCode)
            {
                throw new UpnpException(null, $"{action} 返回 HTTP {(int)response.StatusCode}，且响应不是 XML。", ex);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorCodeText = responseDocument?.Descendants().FirstOrDefault(node => node.Name.LocalName == "errorCode")?.Value;
            var errorDescription = responseDocument?.Descendants().FirstOrDefault(node => node.Name.LocalName == "errorDescription")?.Value;
            int? errorCode = int.TryParse(errorCodeText, out var parsed) ? parsed : null;
            throw new UpnpException(errorCode, errorDescription ?? $"{action} 返回 HTTP {(int)response.StatusCode}。");
        }

        return responseDocument ?? new XDocument();
    }

    public void Dispose() => _httpClient.Dispose();
}

