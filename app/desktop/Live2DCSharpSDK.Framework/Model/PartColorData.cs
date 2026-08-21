using Live2DCSharpSDK.Framework.Rendering;

namespace Live2DCSharpSDK.Framework.Model;

/// <summary>
/// テクスチャの色をRGBAで扱うための構造体
/// </summary>
public record PartColorData
{
    public bool IsOverwritten { get; set; }
    public CubismTextureColor Color = new();
}
