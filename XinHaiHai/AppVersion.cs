namespace XinHaiHai;

/// <summary>
/// 版本号常量。唯一来源与 <c>心海海.csproj</c> 的 <c>&lt;Version&gt;</c> 保持一致,
/// CI(build.yml)会校验两处是否相同,不一致直接失败。
/// 发版流程: 改 csproj 的 Version + 这里 + README → commit → tag v{版本号} → 打 Release 附件。
/// </summary>
public static class AppVersion
{
    public const string Version = "1.7.6";
    public const string Tag = "v" + Version;
    public static string Url => "https://github.com/Xinhaihai-Xinhaihai/Table_pet_xinhaihai/releases/tag/" + Tag;
}
