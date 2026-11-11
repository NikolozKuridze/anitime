using Anitime.Application.Abstractions;
using Anitime.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Anitime.Infrastructure.Storage;

public sealed class MinioFileStorageService : IFileStorageService
{
    private readonly IMinioClient _client;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioFileStorageService> _logger;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketEnsured;

    public MinioFileStorageService(IOptions<MinioOptions> options, ILogger<MinioFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.UseSsl)
            .Build();
    }

    public async Task<string> UploadAsync(string objectName, Stream stream, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType), cancellationToken);

        return objectName;
    }

    public async Task<Stream?> OpenReadAsync(string? objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        await EnsureBucketAsync(cancellationToken);
        var memory = new MemoryStream();
        try
        {
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memory)), cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }

        memory.Position = 0;
        return memory;
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketEnsured)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketEnsured)
            {
                return;
            }

            var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.BucketName), cancellationToken);
            if (!exists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.BucketName), cancellationToken);
                _logger.LogInformation("Created MinIO bucket {BucketName}", _options.BucketName);
            }

            _bucketEnsured = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    public async Task DeleteAsync(string? objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        await EnsureBucketAsync(cancellationToken);
        await _client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName), cancellationToken);
    }
}
