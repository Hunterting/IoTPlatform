using System.Globalization;
using System.Text.RegularExpressions;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣二开设备固件版本号。
/// 设备 <c>getDevInfo</c> 应答中的 <c>version</c> 形如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>，
/// 其中 <c>SWITCH-EC618X-R24-O</c> 为产品线前缀，<c>4.0.8</c> 为语义化版本。
/// 部分参数有最低固件要求（如 <c>q</c> 需 v4.0.20+，<c>uploadEnable</c> 需 v5.0.1+），
/// 本类型用于做「当前固件是否满足最低要求」的比较。
/// </summary>
public sealed class AnShengFirmwareVersion : IComparable<AnShengFirmwareVersion>, IEquatable<AnShengFirmwareVersion>
{
    /// <summary>匹配版本尾段 <c>V4.0.8</c> / <c>v4.0</c> / <c>4.0.8.1</c>。</summary>
    private static readonly Regex VersionRegex = new(
        @"[vV]?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?:\.(?<build>\d+))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>原始版本字符串（未做任何裁剪）。</summary>
    public string Raw { get; }

    /// <summary>产品线前缀，如 <c>SWITCH-EC618X-R24-O</c>；无前缀时为空串。</summary>
    public string Prefix { get; }

    /// <summary>主版本号。</summary>
    public int Major { get; }

    /// <summary>次版本号。</summary>
    public int Minor { get; }

    /// <summary>修订号，缺省为 0。</summary>
    public int Patch { get; }

    /// <summary>构建号，缺省为 0。</summary>
    public int Build { get; }

    private AnShengFirmwareVersion(string raw, string prefix, int major, int minor, int patch, int build)
    {
        Raw = raw;
        Prefix = prefix;
        Major = major;
        Minor = minor;
        Patch = patch;
        Build = build;
    }

    /// <summary>
    /// 解析版本字符串。
    /// </summary>
    /// <param name="raw">原始版本串，如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>、<c>V4.0.20</c>、<c>5.0.1</c>。</param>
    /// <param name="version">解析成功时返回的版本对象。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParse(string? raw, out AnShengFirmwareVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim();
        var match = VersionRegex.Match(text);
        if (!match.Success) return false;

        var major = ParseGroup(match, "major");
        var minor = ParseGroup(match, "minor");
        var patch = ParseGroup(match, "patch");
        var build = ParseGroup(match, "build");

        var prefix = text[..match.Index].TrimEnd('-', '_', ' ', '.');
        version = new AnShengFirmwareVersion(text, prefix, major, minor, patch, build);
        return true;
    }

    /// <summary>
    /// 解析版本字符串，失败抛异常。
    /// </summary>
    /// <param name="raw">原始版本串。</param>
    /// <returns>版本对象。</returns>
    /// <exception cref="FormatException">无法识别版本格式时抛出。</exception>
    public static AnShengFirmwareVersion Parse(string raw)
    {
        if (!TryParse(raw, out var version) || version == null)
        {
            throw new FormatException($"无法解析安圣固件版本号: {raw}");
        }

        return version;
    }

    /// <summary>
    /// 比较两个版本串，语义等价于 <c>current.CompareTo(other)</c>。
    /// 任一侧无法解析时返回 0（视为「无法判断，不拦截」）。
    /// </summary>
    /// <param name="current">当前版本串。</param>
    /// <param name="other">目标版本串。</param>
    /// <returns>负数 / 0 / 正数。</returns>
    public static int Compare(string? current, string? other)
    {
        if (!TryParse(current, out var a) || a == null) return 0;
        if (!TryParse(other, out var b) || b == null) return 0;
        return a.CompareTo(b);
    }

    /// <summary>
    /// 判断 <paramref name="current"/> 是否 &gt;= <paramref name="minimum"/>。
    /// 当 <paramref name="minimum"/> 为空表示无版本要求，直接返回 true；
    /// 当 <paramref name="current"/> 无法解析（设备未上报 version）时返回 true，避免误拦截。
    /// </summary>
    /// <param name="current">设备当前版本串。</param>
    /// <param name="minimum">最低要求版本串。</param>
    /// <returns>满足要求返回 true。</returns>
    public static bool Satisfies(string? current, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        if (!TryParse(minimum, out var min) || min == null) return true;
        if (!TryParse(current, out var cur) || cur == null) return true;
        return cur.CompareTo(min) >= 0;
    }

    /// <summary>
    /// 判断当前实例是否 &gt;= 指定最低版本。
    /// </summary>
    /// <param name="minimum">最低要求版本串。</param>
    /// <returns>满足要求返回 true。</returns>
    public bool AtLeast(string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        if (!TryParse(minimum, out var min) || min == null) return true;
        return CompareTo(min) >= 0;
    }

    /// <inheritdoc />
    public int CompareTo(AnShengFirmwareVersion? other)
    {
        if (other == null) return 1;

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        return Build.CompareTo(other.Build);
    }

    /// <inheritdoc />
    public bool Equals(AnShengFirmwareVersion? other) => other != null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AnShengFirmwareVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Build);

    /// <inheritdoc />
    public override string ToString()
        => string.IsNullOrEmpty(Prefix)
            ? $"V{Major}.{Minor}.{Patch}"
            : $"{Prefix}-V{Major}.{Minor}.{Patch}";

    /// <summary>大于运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>left &gt; right。</returns>
    public static bool operator >(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) > 0;

    /// <summary>小于运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>left &lt; right。</returns>
    public static bool operator <(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) < 0;

    /// <summary>大于等于运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>left &gt;= right。</returns>
    public static bool operator >=(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) >= 0;

    /// <summary>小于等于运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>left &lt;= right。</returns>
    public static bool operator <=(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) <= 0;

    /// <summary>相等运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>版本相同返回 true。</returns>
    public static bool operator ==(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) == 0;

    /// <summary>不等运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>版本不同返回 true。</returns>
    public static bool operator !=(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
        => Comparer(left, right) != 0;

    private static int Comparer(AnShengFirmwareVersion? left, AnShengFirmwareVersion? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        return left.CompareTo(right);
    }

    private static int ParseGroup(Match match, string name)
    {
        var group = match.Groups[name];
        if (!group.Success) return 0;
        return int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
