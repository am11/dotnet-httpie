﻿// Copyright (c) Weihan Li.All rights reserved.
// Licensed under the MIT license.

using HTTPie.Abstractions;
using HTTPie.Utilities;

namespace HTTPie.Implement;

public sealed class CurlParser : ICurlParser
{
    public HttpRequestMessage Parse(string curlScript)
    {
        Guard.NotNullOrEmpty(curlScript);
        var normalizedScript = curlScript
            .Replace("\\\n", " ")
            .Replace("\\\r\n", " ")
            .Replace("\r\n", " ")
            .Replace("\n ", " ")
            .Trim();
        if (!normalizedScript.StartsWith("curl "))
        {
            throw new ArgumentException($"Invalid curl script: {curlScript}", nameof(curlScript));
        }

        var splits = LineParser.ParseLine(normalizedScript).ToArray();
        string requestMethod = string.Empty, requestBody = string.Empty;
        Uri? uri = null;
        var headers = new List<KeyValuePair<string, string>>();
        for (var i = 1; i < splits.Length; i++)
        {
            var item = splits[i].Trim('\'', '"');

            // url
            if (uri is null && Uri.TryCreate(item, UriKind.Absolute, out uri))
            {
                continue;
            }

            // Get Request method
            if (item is "-X")
            {
                i++;

                if (i < splits.Length)
                {
                    item = splits[i].Trim('\'', '"');
                    if (Helpers.HttpMethods.Contains(item))
                        requestMethod = item;
                }

                continue;
            }

            // request body
            if (item is "-d")
            {
                i++;
                if (i < splits.Length)
                {
                    item = splits[i].Trim('\'', '"');
                    requestBody = item;
                }

                continue;
            }

            // headers
            if (item is "-H")
            {
                i++;
                if (i < splits.Length)
                {
                    var header = splits[i].Trim('\'', '"');
                    var headerSplits = header.Split(':', 2, StringSplitOptions.TrimEntries);
                    headers.Add(new KeyValuePair<string, string>(headerSplits[0],
                        headerSplits.Length > 1 ? headerSplits[1] : string.Empty));
                }

                continue;
            }
        }

        if (string.IsNullOrEmpty(requestMethod)) requestMethod = "GET";

        if (uri is null) throw new ArgumentException("Url info not found");

        var request = new HttpRequestMessage(new HttpMethod(requestMethod), uri);
        // request body
        if (!string.IsNullOrEmpty(requestBody))
        {
            request.Content = new StringContent(requestBody);
        }

        // headers
        foreach (var headerGroup in headers.GroupBy(x => x.Key))
        {
            request.TryAddHeader(
                headerGroup.Key,
                headerGroup.Select(x => x.Value).StringJoin(",")
            );
        }

        return request;
    }
}
