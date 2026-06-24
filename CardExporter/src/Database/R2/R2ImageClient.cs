/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CardExporter.CLI;
using MTGOSDK.Core.Security;


namespace CardExporter.Database.R2;

internal sealed class R2ImageClient : IAsyncDisposable
{
  private readonly IAmazonS3 _s3;
  private readonly R2Options _options;

  private R2ImageClient(IAmazonS3 s3, R2Options options)
  {
    _s3 = s3;
    _options = options;
  }

  public static R2ImageClient Create(R2Options options)
  {
    string accessKeyId = ReadRequiredSetting("CF_S3_Access_Key_ID");
    string secretAccessKey = ReadRequiredSetting("CF_S3_Secret_Access_Key");

    var config = new AmazonS3Config
    {
      ServiceURL = options.EndpointUrl,
      ForcePathStyle = true,
      AuthenticationRegion = "auto"
    };
    var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
    return new R2ImageClient(new AmazonS3Client(credentials, config), options);
  }

  public async Task<IReadOnlyList<R2Object>> ListObjectsAsync()
  {
    var rows = new List<R2Object>();
    string? continuationToken = null;
    do
    {
      ListObjectsV2Response response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
      {
        BucketName = _options.BucketName,
        ContinuationToken = continuationToken
      });

      foreach (S3Object item in response.S3Objects)
      {
        rows.Add(new R2Object(
          item.Key,
          new DateTimeOffset(item.LastModified.ToUniversalTime()),
          item.Size,
          item.ETag
        ));
      }

      continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
    }
    while (!string.IsNullOrEmpty(continuationToken));

    return rows;
  }

  public async Task UploadPngAsync(int catalogId, byte[] imageBytes)
  {
    await UploadPngAsync(catalogId, CardImageKind.Card, imageBytes);
  }

  public async Task UploadPngAsync(int catalogId, CardImageKind kind, byte[] imageBytes)
  {
    await UploadObjectAsync(
      CardImageKey.Create(catalogId, kind),
      "image/png",
      imageBytes
    );
  }

  public async Task UploadObjectAsync(
    string key,
    string contentType,
    byte[] bytes
  )
  {
    await using var stream = new MemoryStream(bytes, writable: false);
    await _s3.PutObjectAsync(new PutObjectRequest
    {
      BucketName = _options.BucketName,
      Key = key,
      InputStream = stream,
      ContentType = contentType,
      UseChunkEncoding = false,
      DisablePayloadSigning = true
    });
  }

  public async Task DeletePngAsync(int catalogId)
  {
    await DeletePngAsync(catalogId, CardImageKind.Card);
  }

  public async Task DeletePngAsync(int catalogId, CardImageKind kind)
  {
    await DeleteObjectAsync(CardImageKey.Create(catalogId, kind));
  }

  private async Task DeleteObjectAsync(string key)
  {
    await _s3.DeleteObjectAsync(new DeleteObjectRequest
    {
      BucketName = _options.BucketName,
      Key = key
    });
  }

  public ValueTask DisposeAsync()
  {
    _s3.Dispose();
    return ValueTask.CompletedTask;
  }

  private static string ReadRequiredSetting(string name)
  {
    string? value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      try
      {
        value = DotEnv.Get(name);
      }
      catch
      {
        value = null;
      }
    }

    if (string.IsNullOrWhiteSpace(value))
    {
      throw new InvalidOperationException($"Missing R2 credential setting {name}.");
    }

    return value;
  }

}

internal sealed record R2Object(
  string Key,
  DateTimeOffset LastModified,
  long Size,
  string ETag
);
