﻿// Copyright (c) Weihan Li. All rights reserved.
// Licensed under the MIT license.

using HTTPie.Abstractions;
using HTTPie.Models;
using HTTPie.Utilities;
using Microsoft.Extensions.Primitives;

namespace HTTPie.Middleware;

public sealed class DownloadMiddleware : IResponseMiddleware
{
    public static readonly Option DownloadOption = new(new[] { "-d", "--download" }, "Download file");
    private static readonly Option ContinueOption = new(new[] { "-c", "--continue" }, "Download file using append mode");
    private static readonly Option<string> OutputOption = new(new[] { "-o", "--output" }, "Output file path");

    public ICollection<Option> SupportedOptions()
    {
        return new[] { DownloadOption, ContinueOption, OutputOption };
    }

    public async Task Invoke(HttpContext context, Func<Task> next)
    {
        var output = context.Request.ParseResult.GetValueForOption(OutputOption);
        if (string.IsNullOrWhiteSpace(output))
        {
            if (context.Response.Headers.TryGetValue(Constants.ContentDispositionHeaderName,
                    out var dispositionHeaderValues))
            {
                output = GetFileNameFromContentDispositionHeader(dispositionHeaderValues);
            }

            if (output.IsNullOrWhiteSpace())
            {
                // guess a file name
                context.Response.Headers.TryGetValue(Constants.ContentTypeHeaderName, out var contentType);
                output = GetFileNameFromUrl(context.Request.Url, contentType);
            }
        }
        var fileName = output.GetValueOrDefault($"{DateTime.Now:yyyyMMdd-HHmmss}.tmp");
        if (context.Request.ParseResult.HasOption(ContinueOption))
        {
            await File.AppendAllTextAsync(fileName, context.Response.Body).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllBytesAsync(fileName, context.Response.Bytes).ConfigureAwait(false);
        }
        await next();
    }


    private static string? GetFileNameFromContentDispositionHeader(StringValues headerValues)
    {
        const string filenameSeparator = "filename=";

        var value = headerValues.ToString();
        var index = value.IndexOf(filenameSeparator, StringComparison.OrdinalIgnoreCase);
        if (index > 0 && value.Length > index + filenameSeparator.Length)
        {
            return value[(index + filenameSeparator.Length)..].Trim().Trim('.');
        }
        return null;
    }

    private static string GetFileNameFromUrl(string url, string responseContentType)
    {
        var contentType = responseContentType.Split(';')[0].Trim();
        // https://www.nuget.org/profiles/weihanli/avatar?imageSize=512
        var uri = new Uri(url);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        var fileExtension = Path.GetExtension(uri.AbsolutePath);
        var extension = fileExtension.GetValueOrDefault(MimeTypeMap.GetExtension(contentType));
        return $"{fileNameWithoutExt}{extension}";
    }
}
