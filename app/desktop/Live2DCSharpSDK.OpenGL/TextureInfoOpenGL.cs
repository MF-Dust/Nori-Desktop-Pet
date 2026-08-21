using Live2DCSharpSDK.App;

namespace Live2DCSharpSDK.OpenGL;

public class TextureInfoOpenGL(OpenGLApi api) : TextureInfo
{
    public override void Dispose()
    {
        api.DeleteTexture(Id);
    }
}
