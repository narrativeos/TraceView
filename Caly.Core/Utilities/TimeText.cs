// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;

namespace Caly.Core.Utilities;

/// <summary>
/// Short 中文 relative-time wording for 最近打开 / 活动时间 labels
/// (项目主页 recent rows and the 选择项目 dialog list).
/// Input timestamps are UTC, matching <c>ProjectMetadata</c>.
/// </summary>
public static class TimeText
{
    public static string SinceUtc(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        if (span.TotalMinutes < 1)
            return "刚刚";

        if (span.TotalHours < 1)
            return $"{(int)span.TotalMinutes} 分钟前";

        if (span.TotalDays < 1)
            return $"{(int)span.TotalHours} 小时前";

        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays} 天前";

        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)} 周前";

        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
