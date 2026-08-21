using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Rendering;

namespace Live2DCSharpSDK.App;

/// <summary>
/// アプリケーションクラス。
/// Cubism SDK の管理を行う。
/// </summary>
public abstract class LAppDelegate : IDisposable
{
    /// <summary>
    /// テクスチャマネージャー
    /// </summary>
    public LAppTextureManager TextureManager { get; private set; }

    public LAppLive2DManager Live2dManager { get; private set; }

    /// <summary>
    /// View情報
    /// </summary>
    public LAppView View { get; protected set; }

    public CubismTextureColor BGColor { get; set; } = new(0, 0, 0, 0);

    /// <summary>
    /// クリックしているか
    /// </summary>
    private bool _captured;
    /// <summary>
    /// マウスX座標
    /// </summary>
    private float _mouseX;
    /// <summary>
    /// マウスY座標
    /// </summary>
    private float _mouseY;

    /// <summary>
    /// Initialize関数で設定したウィンドウ幅
    /// </summary>
    public int WindowWidth { get; protected set; }
    /// <summary>
    /// Initialize関数で設定したウィンドウ高さ
    /// </summary>
    public int WindowHeight { get; protected set; }

    public abstract void OnUpdatePre();
    public abstract void GetWindowSize(out int width, out int height);
    public abstract CubismRenderer CreateRenderer(CubismModel model);
    public abstract TextureInfo CreateTexture(LAppModel model, int index, int width, int height, IntPtr data);

    public void InitApp()
    {
        TextureManager = new LAppTextureManager(this);

        // ウィンドウサイズ記憶
        GetWindowSize(out int width, out int height);
        WindowWidth = width;
        WindowHeight = height;
        //AppViewの初期化
        View.Initialize();

        //load model
        Live2dManager = new LAppLive2DManager(this);

        LAppPal.DeltaTime = 0;
    }

    /// <summary>
    /// 解放する。
    /// </summary>
    public void Dispose()
    {
        Live2dManager.Dispose();
    }

    public void Resize()
    {
        GetWindowSize(out int width, out int height);
        if ((WindowWidth != width || WindowHeight != height) && width > 0 && height > 0)
        {
            // サイズを保存しておく
            WindowWidth = width;
            WindowHeight = height;
            //AppViewの初期化
            View.Initialize();
        }
    }

    /// <summary>
    /// Need skip
    /// </summary>
    /// <returns></returns>
    public abstract bool RunPre();
    public abstract void RunPost();

    /// <summary>
    /// 実行処理。
    /// </summary>
    public void Run(float tick)
    {
        Resize();

        // 時間更新
        LAppPal.DeltaTime = tick;

        if (RunPre())
        {
            return;
        }

        //描画更新
        View.Render();

        RunPost();
    }

    /// <summary>
    /// OpenGL用 glfwSetMouseButtonCallback用関数。
    /// </summary>
    /// <param name="button">ボタン種類</param>
    /// <param name="action">実行結果</param>
    public void OnMouseCallBack(bool press)
    {
        if (press)
        {
            _captured = true;
            View.OnTouchesBegan(_mouseX, _mouseY);
        }
        else
        {
            if (_captured)
            {
                _captured = false;
                View.OnTouchesEnded(_mouseX, _mouseY);
            }
        }
    }

    /// <summary>
    /// OpenGL用 glfwSetCursorPosCallback用関数。
    /// </summary>
    /// <param name="x">x座標</param>
    /// <param name="y">x座標</param>
    public void OnMouseCallBack(float x, float y)
    {
        if (!_captured)
        {
            return;
        }

        _mouseX = x;
        _mouseY = y;

        View.OnTouchesMoved(_mouseX, _mouseY);
    }
}
