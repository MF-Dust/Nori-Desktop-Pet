using Live2DCSharpSDK.Framework.Model;

namespace Live2DCSharpSDK.Framework.Effect;

/// <summary>
/// 呼吸機能を提供する。
/// </summary>
public class CubismBreath
{
    /// <summary>
    /// Handles the breathing effect
    /// </summary>
    public required List<BreathParameterData> Parameters { get; init; }
    /// <summary>
    /// 積算時間[秒]
    /// </summary>
    private float _currentTime;

    /// <summary>
    /// モデルのパラメータを更新する。
    /// </summary>
    /// <param name="model">対象のモデル</param>
    /// <param name="deltaTimeSeconds">デルタ時間[秒]</param>
    public void UpdateParameters(CubismModel model, float deltaTimeSeconds)
    {
        _currentTime += deltaTimeSeconds;

        float t = _currentTime * 2.0f * 3.14159f;

        foreach (var item in Parameters)
        {
            model.AddParameterValue(item.ParameterId, item.Offset +
                (item.Peak * MathF.Sin(t / item.Cycle)), item.Weight);
        }
    }
}
