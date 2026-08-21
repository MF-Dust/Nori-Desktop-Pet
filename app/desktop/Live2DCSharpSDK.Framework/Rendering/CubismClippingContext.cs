using Live2DCSharpSDK.Framework.Math;
using Live2DCSharpSDK.Framework.Type;

namespace Live2DCSharpSDK.Framework.Rendering;

public unsafe abstract class CubismClippingContext(CubismClippingManager manager, int* clippingDrawableIndices, int clipCount)
{
    /// <summary>
    /// 現在の描画状態でマスクの準備が必要ならtrue
    /// </summary>
    public bool IsUsing;
    /// <summary>
    /// クリッピングマスクのIDリスト
    /// </summary>
    public unsafe int* ClippingIdList = clippingDrawableIndices;
    /// <summary>
    /// クリッピングマスクの数
    /// </summary>
    public int ClippingIdCount = clipCount;
    /// <summary>
    /// RGBAのいずれのチャンネルにこのクリップを配置するか(0:R , 1:G , 2:B , 3:A)
    /// </summary>
    public int LayoutChannelIndex = 0;
    /// <summary>
    /// マスク用チャンネルのどの領域にマスクを入れるか(View座標-1..1, UVは0..1に直す)
    /// </summary>
    public RectF LayoutBounds = new();
    /// <summary>
    /// このクリッピングで、クリッピングされる全ての描画オブジェクトの囲み矩形（毎回更新）
    /// </summary>
    public RectF AllClippedDrawRect = new();
    /// <summary>
    /// マスクの位置計算結果を保持する行列
    /// </summary>
    public CubismMatrix44 MatrixForMask = new();
    /// <summary>
    /// 描画オブジェクトの位置計算結果を保持する行列
    /// </summary>
    public CubismMatrix44 MatrixForDraw = new();
    /// <summary>
    /// このマスクにクリップされる描画オブジェクトのリスト
    /// </summary>
    public List<int> ClippedDrawableIndexList = [];
    /// <summary>
    /// このマスクが割り当てられるレンダーテクスチャ（フレームバッファ）やカラーバッファのインデックス
    /// </summary>
    public int BufferIndex;

    public CubismClippingManager Manager { get; } = manager;

    /// <summary>
    /// このマスクにクリップされる描画オブジェクトを追加する
    /// </summary>
    /// <param name="drawableIndex">クリッピング対象に追加する描画オブジェクトのインデックス</param>
    public void AddClippedDrawable(int drawableIndex)
    {
        ClippedDrawableIndexList.Add(drawableIndex);
    }
}
