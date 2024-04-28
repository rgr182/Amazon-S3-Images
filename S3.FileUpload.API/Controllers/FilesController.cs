using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using S3.FileUpload.API.Models;

namespace S3.FileUpload.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private const string BucketName = "perritosvacasa";
        private readonly IAmazonS3 _s3Client;

        public FilesController(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
            // Configurar el cliente de Amazon S3 para utilizar Signature Version 4
            AmazonS3Config config = new AmazonS3Config
            {
                SignatureVersion = "4" // Opcionalmente puedes usar SignatureVersion.V4
            };
            _s3Client = new AmazonS3Client(config);
        }

        private async Task<bool> BucketExists()
        {
            var response = await _s3Client.ListBucketsAsync();
            return response.Buckets.Any(b => b.BucketName == BucketName);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFileAsync(IFormFile file, string? prefix)
        {
            if (!await BucketExists())
            {
                return BadRequest(new { Message = $"Bucket {BucketName} does not exist." });
            }

            var key = string.IsNullOrEmpty(prefix) ? file.FileName : $"{prefix?.TrimEnd('/')}/{file.FileName}";

            using (var stream = file.OpenReadStream())
            {
                var request = new PutObjectRequest
                {
                    BucketName = BucketName,
                    Key = key,
                    InputStream = stream
                };
                request.Metadata.Add("Content-Type", file.ContentType);

                await _s3Client.PutObjectAsync(request);
            }

            // Construct URL
            var url = $"https://{BucketName}.s3.amazonaws.com/{key}";

            return Ok(new { Message = $"File {key} uploaded to S3 successfully!", Url = url });
        }

        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllFilesAsync(string? prefix)
        {
            if (!await BucketExists())
            {
                return BadRequest(new { Message = $"Bucket {BucketName} does not exist." });
            }

            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = prefix
            };

            var response = await _s3Client.ListObjectsV2Async(request);

            var s3Objects = response.S3Objects.Select(s =>
            {
                var urlRequest = new GetPreSignedUrlRequest
                {
                    BucketName = BucketName,
                    Key = s.Key,
                    Expires = DateTime.UtcNow.AddSeconds(604800) // 7 días
                };

                return new S3ObjectDto
                {
                    Name = s.Key,
                    PresignedUrl = _s3Client.GetPreSignedURL(urlRequest),
                };
            });

            return Ok(new { Objects = s3Objects });
        }

        [HttpGet("get-by-key")]
        public async Task<IActionResult> GetFileByKeyAsync(string key)
        {
            if (!await BucketExists())
            {
                return BadRequest(new { Message = $"Bucket {BucketName} does not exist." });
            }

            try
            {
                var response = await _s3Client.GetObjectMetadataAsync(BucketName, key);

                using (var responseStream = await _s3Client.GetObjectStreamAsync(BucketName, key, null))
                {
                    return File(responseStream, response.Headers.ContentType);
                }
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return BadRequest(new { Message = $"Object {key} does not exist." });
            }
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFileAsync(string key)
        {
            if (!await BucketExists())
            {
                return BadRequest(new { Message = $"Bucket {BucketName} does not exist." });
            }

            try
            {
                var response = await _s3Client.DeleteObjectAsync(BucketName, key);

                return NoContent();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return BadRequest(new { Message = $"Object {key} does not exist." });
            }
        }
    }
}
