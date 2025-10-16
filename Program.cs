//  dotnet lambda package --output-package function.zip

using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using GBX.NET;
using GBX.NET.LZO;
using GBX.NET.NewtonsoftJson.Converters;
using GBX.NET.ZLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using PartialObjectExtractor;
using JsonSerializer = System.Text.Json.JsonSerializer;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CloudGbxQuery;

public class Function {
    private static readonly HttpClient Http = new();
    private const int MaxFileSizeBytes = 6 * 1024 * 1024;
    private const int MaxResponseSizeBytes = 6 * 1024 * 1024;

    public async Task<APIGatewayProxyResponse > FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) {
        var method = request.HttpMethod;
        var queryParams = request.QueryStringParameters ?? new Dictionary<string, string>();

        try {
            var (fields, fileStream) = method switch {
                "GET" => await HandleGetRequest(queryParams),
                "POST" => await HandlePostRequest(request, queryParams),
                _ => throw new ArgumentException("Only GET and POST requests are supported.")
            };

            var result = await ProcessGbxFile(fileStream, fields);

            var responseBody = result.ToString(Formatting.None);
            if (responseBody.Length > MaxResponseSizeBytes) {
                return new APIGatewayProxyResponse {
                    StatusCode = 413,
                    Body = JsonSerializer.Serialize(new { error = "Response size exceeds 6MB limit." }),
                };
            }

            return new APIGatewayProxyResponse {
                StatusCode = 200,
                Body = responseBody,
                Headers = new Dictionary<string, string> {
                    { "Content-Type", "application/json" }
                }
            };
        }
        catch (ArgumentException ex) {
            return new APIGatewayProxyResponse {
                StatusCode = 400,
                Body = JsonSerializer.Serialize(new { error = ex.Message }),
            };
        }
        catch (Exception ex) {
            context.Logger.LogError(ex.ToString());
            return new APIGatewayProxyResponse {
                StatusCode = 500,
                Body = JsonSerializer.Serialize(new { error = "Failed to process file", details = ex.Message })
            };
        }
    }

    private async Task<(List<string> fields, MemoryStream fileStream)> HandleGetRequest(
        IDictionary<string, string> queryParams) {
        if (!queryParams.ContainsKey("fields")) {
            throw new ArgumentException("'fields' query parameter is missing.");
        }

        if (!queryParams.ContainsKey("url")) {
            throw new ArgumentException("'url' query parameter is missing.");
        }

        var fields = ParseFieldsFromString(queryParams["fields"]);
        var url = WebUtility.UrlDecode(queryParams["url"]);
        var fileStream = await DownloadFile(url);

        return (fields, fileStream);
    }

    private async Task<(List<string> fields, MemoryStream fileStream)> HandlePostRequest(
        APIGatewayProxyRequest request,
        IDictionary<string, string> queryParams) {
        
        // Case 1: fields in query param + file in body
        if (queryParams.ContainsKey("fields")) {
            var fields = ParseFieldsFromString(queryParams["fields"]);
            var fileStream = ReadBodyAsFile(request);
            return (fields, fileStream);
        }

        // Case 2: url and fields in JSON body
        if (!string.IsNullOrEmpty(request.Body)) {
            var body = JObject.Parse(request.Body);
            
            if (body["url"] != null && body["fields"] != null) {
                var fields = ParseFieldsFromJson(body["fields"]!);
                var url = body["url"]!.ToString();
                var fileStream = await DownloadFile(url);
                return (fields, fileStream);
            }
        }

        throw new ArgumentException("Invalid POST request");
    }

    private async Task<MemoryStream> DownloadFile(string url) {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        ms.Position = 0;
        return ms;
    }

    private MemoryStream ReadBodyAsFile(APIGatewayProxyRequest request) {
        if (string.IsNullOrEmpty(request.Body)) {
            throw new ArgumentException("Request body is empty.");
        }

        var fileBytes = request.IsBase64Encoded 
            ? Convert.FromBase64String(request.Body) 
            : System.Text.Encoding.UTF8.GetBytes(request.Body);

        if (fileBytes.Length > MaxFileSizeBytes) {
            throw new ArgumentException($"File size exceeds {MaxFileSizeBytes / 1024 / 1024}MB limit.");
        }

        var ms = new MemoryStream(fileBytes);
        ms.Position = 0;
        return ms;
    }

    private List<string> ParseFieldsFromString(string fieldsParam) {
        return fieldsParam
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(WebUtility.UrlDecode)
            .ToList()!;
    }

    private List<string> ParseFieldsFromJson(JToken fieldsToken) {
        // Handle array format: ["field1", "field2"]
        if (fieldsToken is JArray array) {
            return array.Select(t => t.ToString()).ToList();
        }
        
        // Handle string format: "field1,field2"
        return ParseFieldsFromString(fieldsToken.ToString());
    }

    private async Task<JObject> ProcessGbxFile(MemoryStream fileStream, List<string> fields) {
        var settings = new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore
        };

        settings.Converters.Add(new TimeInt32JsonConverter());
        settings.Converters.Add(new NullableTimeInt32JsonConverter());
        settings.Converters.Add(new StringEnumConverter());
        
        var extractor = new PartialExtractor(settings);

        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();

        var gbx = await Gbx.ParseNodeAsync(fileStream);
        return extractor.ExtractPaths(gbx, fields);
    }
}