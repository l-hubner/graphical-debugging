//------------------------------------------------------------------------------
// <copyright file="ExpressionLoader_BoostGil.cs">
//     Copyright (c) Adam Wulkiewicz.
// </copyright>
//------------------------------------------------------------------------------

using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Task = System.Threading.Tasks.Task;

namespace GraphicalDebugging
{
    partial class ExpressionLoader
    {
        class BitmapSourceLoader : LoaderR<ExpressionDrawer.Image>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Image; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    return id == "OpenCvSharp.Mat"
                         ? new BitmapSourceLoader()
                         : null;
                }
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return null;
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                // NOTE: If the image is not created at the point of debugging, so the variable is
                // uninitialized, the size may be out of bounds of int32 range. In this case the
                // exception is thrown here and this is ok. However if there is some garbage in
                // memory random size could be loaded here. Then also the memory probably points
                // to some random place in memory (maybe protected?) so the result will probably
                // be another exception which is fine or an image containing noise from memory.
                ulong dataStart = ExpressionParser.GetPointer(debugger, name + ".DataStart");
                ulong dataEnd = ExpressionParser.GetPointer(debugger, name + ".DataEnd");
                ulong length = dataEnd - dataStart;
                int cols = ExpressionParser.LoadSize(debugger, name + ".Cols");
                int rows = ExpressionParser.LoadSize(debugger, name + ".Rows");
                EnvDTE.Expression exp = debugger.GetExpression("(int)(" + name + ".Type())");

                MatType mType = MatType.CV_8UC1;
                if (exp.IsValidValue)
                {
                    mType = (MatType)int.Parse(exp.Value);
                }

                byte[] memory = new byte[length];
                bool isLoaded = false;
                if (mreader != null)
                {
                    ulong address = ExpressionParser.GetValueAddress(debugger, name + ".DataPointer[0]");
                    if (address == 0)
                        return null;

                    isLoaded = mreader.ReadBytes(address, memory);
                }

                var pixelFormat = MatTypeToPixelFormat(mType);
                Bitmap bmp = new Bitmap(
                  cols,
                  rows,
                  pixelFormat);

                BitmapData data = bmp.LockBits(
                  new Rectangle(System.Drawing.Point.Empty, bmp.Size),
                  ImageLockMode.WriteOnly,
                  pixelFormat);

                Marshal.Copy(memory, 0, data.Scan0, (int)length);

                bmp.UnlockBits(data);

                return new ExpressionDrawer.Image(bmp);
            }
            
            private PixelFormat MatTypeToPixelFormat(MatType type)
            {
                if (type == MatType.CV_8UC1)
                    return PixelFormat.Format8bppIndexed;
                else if (type == MatType.CV_8UC3)
                    return PixelFormat.Format24bppRgb;
                else if (type == MatType.CV_16UC1)
                    return PixelFormat.Format16bppGrayScale;
                else if (type == MatType.CV_16UC3)
                    return PixelFormat.Format48bppRgb;
                else
                    throw new NotSupportedException();                
            }
        }
    }
}
