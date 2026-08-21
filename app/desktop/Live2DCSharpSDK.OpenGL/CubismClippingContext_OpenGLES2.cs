using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Rendering;

namespace Live2DCSharpSDK.OpenGL;

public unsafe class CubismClippingContext_OpenGLES2(CubismClippingManager manager, CubismModel model,
    int* clippingDrawableIndices, int clipCount) : CubismClippingContext(manager, clippingDrawableIndices, clipCount)
{

}