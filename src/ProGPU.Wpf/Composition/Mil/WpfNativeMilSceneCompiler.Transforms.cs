using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompiler
{
    private sealed partial class BuildContext
    {
        private readonly HashSet<object> _activeTransforms = new(ReferenceEqualityComparer.Instance);
        private int _transformEdges;
        private int _transformResources;

        // Capture one original resource once in this batch. Publication still
        // occurs only after the complete visual graph succeeds; a failure here
        // cannot publish private writer bytes or mutate a live native channel.
        private uint ResolveOriginalTransform(object resource)
        {
            if (_activeTransforms.Contains(resource))
                throw new InvalidOperationException("The portable transform source graph contains a cycle.");
            if (_transformHandles.TryGetValue(resource, out uint existing))
                return existing;
            if (_activeTransforms.Count >= 256 ||
                ++_transformResources > PortableTransformGroup.MaximumChildCount)
                throw new InvalidOperationException("The portable transform source graph exceeds the native resource/depth budget.");
            _activeTransforms.Add(resource);
            try
            {
                PortableTransform captured;
                if (resource is IPortableTransformSource original)
                {
                    if (!original.TryGetPortableTransform(out captured) || captured is null)
                        throw MissingContract(nameof(IPortableTransformSource));
                }
                else if (resource is IPortableTransformMatrixSource matrixSource &&
                    matrixSource.TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix))
                {
                    // Explicit external matrix providers have only this matrix
                    // contract. Never infer named primitives or child history.
                    captured = new PortableMatrixTransform(matrix);
                }
                else
                    throw MissingContract(nameof(IPortableTransformMatrixSource));

                uint[] children = [];
                if (captured is PortableTransformGroup group)
                {
                    _transformEdges = checked(_transformEdges + group.Children.Count);
                    if (_transformEdges > PortableTransformGroup.MaximumChildCount)
                        throw new InvalidOperationException("The portable transform source graph exceeds the native child budget.");
                    children = new uint[group.Children.Count];
                    for (int index = 0; index < children.Length; ++index)
                        children[index] = ResolveOriginalTransform(group.Children[index]);
                }

                NativeMilResourceType kind = captured switch
                {
                    PortableMatrixTransform => NativeMilResourceType.MatrixTransform,
                    PortableTranslateTransform => NativeMilResourceType.TranslateTransform,
                    PortableScaleTransform => NativeMilResourceType.ScaleTransform,
                    PortableRotateTransform => NativeMilResourceType.RotateTransform,
                    PortableSkewTransform => NativeMilResourceType.SkewTransform,
                    PortableTransformGroup => NativeMilResourceType.TransformGroup,
                    _ => throw new NotSupportedException("Unknown original portable transform resource contract.")
                };
                uint handle = NextHandle();
                Batch.CreateResource(handle, kind);
                switch (captured)
                {
                    case PortableMatrixTransform value:
                        PortableMatrix3x2 m = value.Matrix;
                        Batch.SetMatrixTransform(handle, new NativeMilMatrix3x2(
                            m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY));
                        break;
                    case PortableTranslateTransform value:
                        Batch.SetTranslateTransform(handle, value.X, value.Y);
                        break;
                    case PortableScaleTransform value:
                        Batch.SetScaleTransform(handle, value.ScaleX, value.ScaleY, value.CenterX, value.CenterY);
                        break;
                    case PortableRotateTransform value:
                        Batch.SetRotateTransform(handle, value.Angle, value.CenterX, value.CenterY);
                        break;
                    case PortableSkewTransform value:
                        Batch.SetSkewTransform(handle, value.AngleX, value.AngleY, value.CenterX, value.CenterY);
                        break;
                    case PortableTransformGroup:
                        Batch.SetTransformGroup(handle, children);
                        break;
                }
                _transformHandles.Add(resource, handle);
                return handle;
            }
            finally { _activeTransforms.Remove(resource); }
        }
    }
}
