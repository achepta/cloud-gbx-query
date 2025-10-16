# CloudGbxQuery

An AWS Lambda function that allows querying GBX (GameBox) files using JSONPath expressions. Extract specific data from TrackMania replay files without downloading the entire file structure.

## Overview

CloudGbxQuery provides a serverless API for extracting partial data from GBX files. It supports multiple input methods and uses JSONPath syntax to specify exactly which fields you want to retrieve,
significantly reducing response payload sizes.

**Built with:**

- AWS Lambda (.NET 8)
- [GBX.NET](https://github.com/BigBang1112/gbx-net) for GBX parsing
- [PartialObjectExtractor](https://github.com/achepta/csharp-partial-object-extractor) for JSONPath querying

## API Usage

### Method 1: GET with URL

Query a GBX file from a URL using query parameters.

```bash
GET https://function.url/?fields=%24.Ghosts%5B0%5D.Checkpoints%5B*%5D.Time&url=https%3A%2F%2Ftmnf.exchange%2Frecordgbx%2F12864099
```

**Parameters:**

- `url` - URL to download the GBX file from (URL encoded)
- `fields` - Comma-separated JSONPath expressions (URL encoded per field)

### Method 2: POST with JSON Body

Send both URL and fields in a JSON body.

```bash
POST https://function.url/
Content-Type: application/json

{
    "fields": [
        "$.Ghosts[0].Checkpoints[*].Time"
    ],
    "url": "https://tmnf.exchange/recordgbx/12864099"
}
```

**Body format:**

- `url` - URL to download the GBX file from
- `fields` - Array of JSONPath expressions

### Method 3: POST with File Upload

Upload a GBX file directly with fields in query parameters.

```bash
POST https://your-function-url.lambda-url.us-east-1.on.aws/?fields=%24.Ghosts%5B0%5D.Checkpoints%5B*%5D.Time
Content-Type: application/octet-stream

[binary GBX file data]
```

**Parameters:**

- `fields` - Comma-separated JSONPath expressions (URL encoded)

**Body:**

- Raw binary GBX file (max 6MB)

For complete JSONPath syntax documentation, see the [PartialObjectExtractor documentation](https://github.com/achepta/csharp-partial-object-extractor).

## Response Format

Successful responses return JSON with the requested fields:

```json
{
  "Ghosts": [
    {
      "Checkpoints": [
        {
          "Time": 7900
        },
        {
          "Time": 13170
        },
        {
          "Time": 16080
        },
        {
          "Time": 24400
        },
        {
          "Time": 29440
        },
        {
          "Time": 36190
        }
      ]
    }
  ]
}
```

Error responses:

```json
{
  "error": "Error message",
  "details": "Additional details if available"
}
```

## Limits

- **Upload size**: 6MB maximum for direct file uploads
- **Response size**: 6MB maximum
- **Downloaded files**: No size limit (but Lambda has 512MB memory by default)
- **Timeout**: 30 seconds
- **Concurrency**: 1 (only one request processed at a time)

## Deployment

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [AWS SAM CLI](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html)
- [AWS CLI](https://aws.amazon.com/cli/) configured with credentials
- [Amazon.Lambda.Tools](https://github.com/aws/aws-extensions-for-dotnet-cli)

```bash
dotnet tool install -g Amazon.Lambda.Tools
dotnet add package PartialObjectExtractor --version 1.0.0
```

### Build and Deploy

1. **Build the Lambda package:**
   ```bash
   dotnet lambda package --output-package function.zip
   ```

2. **Deploy with SAM (first time):**
   ```bash
   sam deploy --guided
   ```
   SAM will create a `samconfig.toml` with your settings.


3. **Subsequent deployments:**
   ```bash
   dotnet lambda package --output-package function.zip
   sam deploy
   ```

4. **Get your Function URL:**
   After deployment, the function URL will be in the outputs:
   ```
   CloudGbxQueryFunctionUrl = https://xxxxx.lambda-url.us-east-1.on.aws/
   ```

### Update Configuration

Edit `template.yaml` to modify:

- **Memory**: Change `MemorySize` (default: 512MB)
- **Timeout**: Change `Timeout` (default: 30 seconds)
- **Concurrency**: Change `ReservedConcurrentExecutions` (default: 1)
- **CORS**: Modify `AllowOrigins`, `AllowMethods`, etc.

### Local Testing

You can test locally using SAM:

```bash
sam local start-api
```

⚠️ if SAM doesn't see docker, run in powershell as administrator
```shell
& "C:\Program Files\Amazon\AWSSAMCLI\runtime\python.exe" -m pip install --force-reinstall --upgrade pywin32
```
if didn't help, run also
```shell
& "C:\Program Files\Amazon\AWSSAMCLI\runtime\python.exe" -m pywin32_postinstall -install
```

Then make requests to `http://localhost:3000`.

### Testing Examples

**Using curl:**

```bash
# GET method
curl "http://localhost:3000?url=https://tmnf.exchange/recordgbx/12864099&fields=%24.Ghosts%5B0%5D.Checkpoints%5B*%5D.Time"

# POST with JSON
curl -X POST http://localhost:3000 \
  -H "Content-Type: application/json" \
  -d '{
    "fields": [
        "$.Ghosts[0].Checkpoints[*].Time"
    ],
    "url": "https://tmnf.exchange/recordgbx/12864099"
  }'

# POST with file
curl -X POST "http://localhost:3000/?fields=%24.Ghosts%5B0%5D.Checkpoints%5B*%5D.Time" \
  -H "Content-Type: application/octet-stream" \
  --data-binary "@replay.gbx"
```

## Error Handling

| Status Code | Description                                          |
|-------------|------------------------------------------------------|
| 200         | Success                                              |
| 400         | Bad request (missing parameters, invalid JSON, etc.) |
| 413         | Payload too large (>6MB for uploads or response)     |
| 500         | Internal server error (GBX parsing failed, etc.)     |

