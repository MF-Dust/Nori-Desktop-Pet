namespace Live2DCSharpSDK.Framework.Motion;

/// <summary>
/// CubismMotionQueueManagerで再生している各モーションの管理クラス。
/// </summary>
public class CubismMotionQueueEntry
{
    /// <summary>
    /// モーション
    /// </summary>
    public required ACubismMotion Motion { get; set; }

    /// <summary>
    /// 有効化フラグ
    /// </summary>
    public bool Available { get; set; }
    /// <summary>
    /// 終了フラグ
    /// </summary>
    public bool Finished { get; set; }
    /// <summary>
    /// 開始フラグ（0.9.00以降）
    /// </summary>
    public bool Started { get; set; }
    /// <summary>
    /// モーション再生開始時刻[秒]
    /// </summary>
    public float StartTime { get; set; }
    /// <summary>
    /// フェードイン開始時刻（ループの時は初回のみ）[秒]
    /// </summary>
    public float FadeInStartTime { get; set; }
    /// <summary>
    /// 終了予定時刻[秒]
    /// </summary>
    public float EndTime { get; set; }
    /// <summary>
    /// 時刻の状態[秒]
    /// </summary>
    public float StateTime { get; private set; }
    /// <summary>
    /// 重みの状態
    /// </summary>
    public float StateWeight { get; private set; }
    /// <summary>
    /// 最終のMotion側のチェックした時間
    /// </summary>
    public float LastEventCheckSeconds { get; set; }

    public float FadeOutSeconds { get; private set; }

    public bool IsTriggeredFadeOut { get; private set; }

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public CubismMotionQueueEntry()
    {
        Available = true;
        StartTime = -1.0f;
        EndTime = -1.0f;
    }

    /// <summary>
    /// フェードアウトの開始を設定する。
    /// </summary>
    /// <param name="fadeOutSeconds">フェードアウトにかかる時間[秒]</param>
    public void SetFadeout(float fadeOutSeconds)
    {
        FadeOutSeconds = fadeOutSeconds;
        IsTriggeredFadeOut = true;
    }

    /// <summary>
    /// フェードアウトを開始する。
    /// </summary>
    /// <param name="fadeOutSeconds">フェードアウトにかかる時間[秒]</param>
    /// <param name="userTimeSeconds">デルタ時間の積算値[秒]</param>
    public void StartFadeout(float fadeOutSeconds, float userTimeSeconds)
    {
        float newEndTimeSeconds = userTimeSeconds + fadeOutSeconds;
        IsTriggeredFadeOut = true;

        if (EndTime < 0.0f || newEndTimeSeconds < EndTime)
        {
            EndTime = newEndTimeSeconds;
        }
    }

    /// <summary>
    /// モーションの状態を設定する。
    /// </summary>
    /// <param name="timeSeconds">現在時刻[秒]</param>
    /// <param name="weight">モーションの重み</param>
    public void SetState(float timeSeconds, float weight)
    {
        StateTime = timeSeconds;
        StateWeight = weight;
    }
}
