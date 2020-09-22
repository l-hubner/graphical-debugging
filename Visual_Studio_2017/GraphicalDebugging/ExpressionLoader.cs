﻿//------------------------------------------------------------------------------
// <copyright file="ExpressionLoader.cs">
//     Copyright (c) Adam Wulkiewicz.
// </copyright>
//------------------------------------------------------------------------------

using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace GraphicalDebugging
{
    partial class ExpressionLoader
    {
        private DTE2 dte;
        private Debugger debugger;
        private DebuggerEvents debuggerEvents; // debuggerEvents member is needed for the events to fire properly
        private Loaders loadersCpp;
        private Loaders loadersCS;
        private LoadersCache loadersCacheCpp;
        private LoadersCache loadersCacheCS;

        // TODO: It's not clear what to do with Variant
        // At the initial stage it's not known what is stored in Variant
        // so currently the kind of the stored object is not filtered properly.

        public enum Kind
        {
            Container = 0, MultiPoint, TurnsContainer, ValuesContainer, GeometriesContainer,
            Point, Segment, Box, NSphere, Linestring, Ring, Polygon,
            MultiLinestring, MultiPolygon, MultiGeometry, Turn, OtherGeometry,
            Variant, Image
        };

        public delegate void BreakModeEnteredEventHandler();
        public static event BreakModeEnteredEventHandler BreakModeEntered;

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            loadersCacheCpp.Clear();
            loadersCacheCS.Clear();

            BreakModeEntered?.Invoke();
        }

        public static bool IsBreakMode
        {
            get { return Debugger.CurrentMode == dbgDebugMode.dbgBreakMode; }
        }

        private static ExpressionLoader Instance { get; set; }
        private static Debugger Debugger
        {
            get { return Instance.debugger; }
        }
        
        public static void Initialize(GraphicalWatchPackage package)
        {
            DTE2 dte = package.GetService(typeof(DTE)) as DTE2;
            
            Instance = new ExpressionLoader(dte);
        }

        private ExpressionLoader(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.dte = dte;
            this.debugger = dte.Debugger;
            this.debuggerEvents = this.dte.Events.DebuggerEvents;
            this.debuggerEvents.OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;

            loadersCpp = new Loaders();

            loadersCpp.Add(new BGPoint.LoaderCreator());
            loadersCpp.Add(new BGPointXY.LoaderCreator());
            loadersCpp.Add(new BGSegment.LoaderCreator());
            loadersCpp.Add(new BGReferringSegment.LoaderCreator());
            loadersCpp.Add(new BGBox.LoaderCreator());
            loadersCpp.Add(new BGNSphere.LoaderCreator());
            loadersCpp.Add(new BGMultiPoint.LoaderCreator());
            loadersCpp.Add(new BGLinestring.LoaderCreator());
            loadersCpp.Add(new BGMultiLinestring.LoaderCreator());
            loadersCpp.Add(new BGRing.LoaderCreator());
            loadersCpp.Add(new BGPolygon.LoaderCreator());
            loadersCpp.Add(new BGMultiPolygon.LoaderCreator());
            loadersCpp.Add(new BGBufferedRing.LoaderCreator());
            loadersCpp.Add(new BGBufferedRingCollection.LoaderCreator());

            loadersCpp.Add(new BGIRtree.LoaderCreator());

            loadersCpp.Add(new BPPoint.LoaderCreator());
            loadersCpp.Add(new BPSegment.LoaderCreator());
            loadersCpp.Add(new BPBox.LoaderCreator());
            loadersCpp.Add(new BPRing.LoaderCreator());
            loadersCpp.Add(new BPPolygon.LoaderCreator());

            loadersCpp.Add(new StdPairPoint.LoaderCreator());
            loadersCpp.Add(new StdComplexPoint.LoaderCreator());

            loadersCpp.Add(new BVariant.LoaderCreator());

            loadersCpp.Add(new StdArray.LoaderCreator());
            loadersCpp.Add(new BoostArray.LoaderCreator());
            loadersCpp.Add(new BGVarray.LoaderCreator());
            loadersCpp.Add(new BoostContainerVector.LoaderCreator());
            loadersCpp.Add(new BoostContainerStaticVector.LoaderCreator());
            loadersCpp.Add(new BoostCircularBuffer.LoaderCreator());
            loadersCpp.Add(new StdVector.LoaderCreator());
            loadersCpp.Add(new StdDeque.LoaderCreator());
            loadersCpp.Add(new StdList.LoaderCreator());
            loadersCpp.Add(new StdSet.LoaderCreator());
            loadersCpp.Add(new CArray.LoaderCreator());

            loadersCpp.Add(new BGTurn.LoaderCreator("boost::geometry::detail::overlay::turn_info"));
            loadersCpp.Add(new BGTurn.LoaderCreator("boost::geometry::detail::overlay::traversal_turn_info"));
            loadersCpp.Add(new BGTurn.LoaderCreator("boost::geometry::detail::buffer::buffer_turn_info"));

            loadersCpp.Add(new BGTurnContainer.LoaderCreator());
            loadersCpp.Add(new PointContainer.LoaderCreator());
            loadersCpp.Add(new ValuesContainer.LoaderCreator());
            loadersCpp.Add(new GeometryContainer.LoaderCreator());

            loadersCpp.Add(new BoostGilImage.LoaderCreator());

            loadersCS = new Loaders();

            loadersCS.Add(new CSLinkedList.LoaderCreator());
            loadersCS.Add(new CSList.LoaderCreator());
            loadersCS.Add(new CSArray.LoaderCreator());
            loadersCS.Add(new CSContainerBase.LoaderCreator());

            loadersCS.Add(new PointContainer.LoaderCreator());
            loadersCS.Add(new ValuesContainer.LoaderCreator());
            loadersCS.Add(new GeometryContainer.LoaderCreator());

            loadersCS.Add(new BitmapSourceLoader.LoaderCreator());

            loadersCacheCpp = new LoadersCache();
            loadersCacheCS = new LoadersCache();
        }

        // Expressions utilities

        public static Expression[] GetExpressions(string name, char separator = ';')
        {
            var expr = Debugger.GetExpression(name);
            if (expr.IsValidValue)
                return new Expression[] { expr };

            string[] subnames = name.Split(separator);
            Expression[] exprs = new Expression[subnames.Length];
            for (int i = 0; i < subnames.Length; ++i)
            {
                exprs[i] = Debugger.GetExpression(subnames[i]);
            }

            return exprs;
        }

        public static bool AllValidValues(Expression[] exprs)
        {
            foreach(Expression e in exprs)
                if (!e.IsValidValue)
                    return false;
            return true;
        }

        public static bool AnyValidValue(Expression[] exprs)
        {
            foreach (Expression e in exprs)
                if (e.IsValidValue)
                    return true;
            return false;
        }

        public static string TypeFromExpressions(Expression[] exprs)
        {
            string result = "";
            bool first = true;
            foreach (Expression e in exprs)
            {
                if (first)
                    first = false;
                else
                    result += " ; ";
                result += e.Type;
            }
            return result;
        }

        // Kind Constraints

        public interface IKindConstraint
        {
            bool Check(Kind kind);
        }

        public class KindConstraint : IKindConstraint
        {
            public KindConstraint(Kind kind)
            {
                mKind = kind;
            }

            public bool Check(Kind kind)
            {
                return mKind == kind;
            }

            public Kind Kind { get { return mKind; } }

            Kind mKind;
        }

        public class NonValueKindConstraint : IKindConstraint
        {
            public bool Check(Kind kind)
            {
                return kind != Kind.Container
                    && kind != Kind.ValuesContainer;
            }
        }

        public class DrawableKindConstraint : IKindConstraint
        {
            public bool Check(Kind kind) { return kind != Kind.Container; }
        }

        // IMPORTANT: GeometriesContainer cannot be a Geometry,
        //   otherwise infinite recursion may occur
        public class GeometryKindConstraint : IKindConstraint
        {
            public bool Check(Kind kind)
            {
                return kind != Kind.Container
                    && kind != Kind.ValuesContainer
                    && kind != Kind.GeometriesContainer
                    && kind != Kind.Image;
            }
        }

        public class GeometryOrGeometryContainerKindConstraint : IKindConstraint
        {
            public bool Check(Kind kind)
            {
                return kind != Kind.Container
                    && kind != Kind.ValuesContainer
                    && kind != Kind.Image;
            }
        }

        public class IndexableKindConstraint : IKindConstraint
        {
            public bool Check(Kind kind)
            {
                return kind == Kind.Point
                    || kind == Kind.Box
                    || kind == Kind.Segment;
            }
        }

        public static DrawableKindConstraint AllDrawables { get; } = new DrawableKindConstraint();
        public static GeometryKindConstraint OnlyGeometries { get; } = new GeometryKindConstraint();
        public static GeometryOrGeometryContainerKindConstraint OnlyGeometriesOrGeometryContainer { get; } = new GeometryOrGeometryContainerKindConstraint();
        public static KindConstraint OnlyValuesContainers { get; } = new KindConstraint(Kind.ValuesContainer);
        public static KindConstraint OnlyMultiPoints { get; } = new KindConstraint(Kind.MultiPoint);
        public static IndexableKindConstraint OnlyIndexables { get; } = new IndexableKindConstraint();
        public static NonValueKindConstraint OnlyNonValues { get; } = new NonValueKindConstraint();

        /// <summary>
        /// Loads debugged variable into ExpressionDrawer.IDrawable and additional
        /// geometrical information into Geometry.Traits. These classes then
        /// can be passed into ExpressionDrawer.Draw() in order to draw them
        /// on Graphics surface.
        /// </summary>
        /// <param name="name">Name of variable or actual expression added to watch</param>
        /// <param name="traits">Geometrical traits</param>
        /// <param name="result">An object that can be drawn by ExpressionDrawer</param>
        public static void Load(string name,
                                out Geometry.Traits traits,
                                out ExpressionDrawer.IDrawable result)
        {
            Load(name, AllDrawables, out traits, out result);
        }

        /// <summary>
        /// Loads debugged variable into ExpressionDrawer.IDrawable and additional
        /// geometrical information into Geometry.Traits. These classes then
        /// can be passed into ExpressionDrawer.Draw() in order to draw them
        /// on Graphics surface. This version loads only those kinds of variables
        /// that are defined by KindConstraint.
        /// </summary>
        /// <param name="name">Name of variable or actual expression added to watch</param>
        /// <param name="kindConstraint">Predicate defining the kind of debugged variable</param>
        /// <param name="traits">Geometrical traits</param>
        /// <param name="result">An object that can be drawn by ExpressionDrawer</param>
        public static void Load(string name,
                                IKindConstraint kindConstraint,
                                out Geometry.Traits traits,
                                out ExpressionDrawer.IDrawable result)
        {
            traits = null;
            result = null;

            Expression[] exprs = GetExpressions(name);
            if (exprs.Length < 1 || ! AllValidValues(exprs))
                return;

            string language = Instance.debugger.CurrentStackFrame.Language;
            Loaders loaders = language == "C#" ? Instance.loadersCS : Instance.loadersCpp;

            MemoryReader mreader = null;
            GeneralOptionPage optionPage = Util.GetDialogPage<GeneralOptionPage>();
            if (optionPage == null || optionPage.EnableDirectMemoryAccess)
            {
                mreader = new MemoryReader(Instance.debugger);
            }

            LoadTimeGuard timeGuard = new LoadTimeGuard();
            
            if (exprs.Length == 1)
            {
                DrawableLoader loader = loaders.FindByType(kindConstraint, exprs[0].Name, exprs[0].Type)
                                            as DrawableLoader;
                if (loader == null)
                    return;

                traits = loader.GetTraits(mreader, Instance.debugger,
                                          exprs[0].Name);

                result = loader.Load(mreader, Instance.debugger,
                                     exprs[0].Name, exprs[0].Type,
                                     delegate ()
                                     {
                                        return timeGuard.CheckTimeAndDisplayMsg(name);
                                     });
            }
            else //if (exprs.Length > 1)
            {
                // For now there is only one loader which can handle this case
                var creator = new CoordinatesContainers.LoaderCreator();
                CoordinatesContainers loader = creator.Create(loaders, exprs);

                traits = loader.GetTraits();

                result = loader.Load(mreader, Instance.debugger,
                                     exprs,
                                     delegate ()
                                     {
                                        return timeGuard.CheckTimeAndDisplayMsg(name);
                                     });
            }
        }

        class LoadersCache
        {
            private class Entry
            {
                public Entry(Kind kind, Loader loader)
                {
                    Kind = kind;
                    Loader = loader;
                }

                public Kind Kind;
                public Loader Loader;
            }

            public LoadersCache()
            {
                dict = new Dictionary<string, List<Entry>>();
            }

            public void Add(string type, Kind kind, Loader loader)
            {
                List<Entry> list;
                if (!dict.TryGetValue(type, out list))
                {
                    list = new List<Entry>();
                    dict.Add(type, list);
                }
                // TODO: check if the type/kind is already stored and throw an exception
                //   to detect duplication?
                list.Add(new Entry(kind, loader));
            }

            public Loader Find(string type, IKindConstraint kindConstraint)
            {
                List<Entry> list;
                if (dict.TryGetValue(type, out list))
                {
                    foreach (Entry e in list)
                    {
                        if (kindConstraint.Check(e.Kind))
                            return e.Loader;
                    }
                }
                return null;
            }

            public void Clear()
            {
                dict.Clear();
            }

            private Dictionary<string, List<Entry>> dict;
        }

        /// <summary>
        /// The container of Loaders providing utilities to add and find
        /// Loaders based on Kind.
        /// </summary>
        class Loaders
        {
            static int KindsCount = Enum.GetValues(typeof(Kind)).Length;

            public Loaders()
            {
                lists = new List<LoaderCreator>[KindsCount];
                for (int i = 0; i < KindsCount; ++i)
                    lists[i] = new List<LoaderCreator>();
            }

            public void Add(LoaderCreator loaderCreator)
            {
                int i = (int)loaderCreator.Kind();
                System.Diagnostics.Debug.Assert(0 <= i && i < KindsCount);
                lists[i].Add(loaderCreator);
            }

            /// <summary>
            /// Finds loader by given Kind and C++ or C# type.
            /// </summary>
            /// <param name="kind">Kind of Loader</param>
            /// <param name="name">Name of variable or actual expression added to watch</param>
            /// <param name="type">C++ or C# type of variable</param>
            /// <returns>Loader object or null if not found</returns>
            public Loader FindByType(Kind kind, string name, string type)
            {
                return FindByType(new KindConstraint(kind), name, type);
            }

            /// <summary>
            /// Finds loader by given KindConstraint and C++ or C# type.
            /// </summary>
            /// <param name="kindConstraint">Predicate defining the kind of Loader</param>
            /// <param name="name">Name of variable or actual expression added to watch</param>
            /// <param name="type">C++ or C# type of variable</param>
            /// <returns>Loader object or null if not found</returns>
            public Loader FindByType(IKindConstraint kindConstraint, string name, string type)
            {
                // Check if a Loader is cached for this type
                string language = Instance.debugger.CurrentStackFrame.Language;
                var loadersCache = language == "C#" ? Instance.loadersCacheCS : Instance.loadersCacheCpp;
                Loader loader = loadersCache.Find(type, kindConstraint);
                if (loader != null)
                    return loader;

                // Parse type for qualified identifier
                string id = Util.TypeId(type);

                // Look for loader creator on the list(s)
                if (kindConstraint is KindConstraint)
                {
                    // Single kind required, check only one list
                    Kind kind = (kindConstraint as KindConstraint).Kind;
                    int kindIndex = (int)kind;
                    foreach (LoaderCreator creator in lists[kindIndex])
                    {
                        loader = creator.Create(this, Debugger, name, type, id);
                        if (loader != null)
                        {
                            loadersCache.Add(type, kind, loader);
                            return loader;
                        }
                    }
                }
                else
                {
                    // Multiple kinds may be required, check all of the lists
                    for (int i = 0; i < lists.Length; ++i)
                    {
                        Kind kind = (Kind)i;
                        if (kindConstraint.Check(kind))
                        {
                            foreach (LoaderCreator creator in lists[i])
                            {
                                loader = creator.Create(this, Debugger, name, type, id);
                                if (loader != null)
                                {
                                    loadersCache.Add(type, kind, loader);
                                    return loader;
                                }
                            }
                        }
                    }
                }
                return null;
            }

            public void RemoveUserDefined()
            {
                foreach (List<LoaderCreator> li in lists)
                {
                    List<LoaderCreator> removeList = new List<LoaderCreator>();
                    for (int i = li.Count - 1; i >= 0; --i)
                        if (li[i].IsUserDefined())
                            li.RemoveAt(i);
                }                
            }

            List<LoaderCreator>[] lists;
        }

        /// <summary>
        /// The interface of a loader creator.
        /// </summary>
        interface LoaderCreator
        {
            /// <summary>
            /// Returns true for user-defined Loaders which has to be reloaded
            /// before loading variables.
            /// </summary>
            bool IsUserDefined(); // Instead of this make 2 containers?

            /// <summary>
            /// Returns kind of created Loader.
            /// </summary>
            Kind Kind();

            /// <summary>
            /// Matches type and/or qualified identifier, then creates and initializes
            /// the Loader before it's used to load a debugged variable.
            /// Type and identifier can both receove the same value e.g. unsigned char[4].
            /// </summary>
            /// <param name="type">Full type</param>
            /// <param name="id">Qualified idenifier of type</param>
            Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id);
        }

        /// <summary>
        /// The base Loader class from which all Loaders has to be derived.
        /// </summary>
        abstract class Loader
        {
        }

        /// <summary>
        /// The base class of loaders which can load variables that can be drawn.
        /// </summary>
        abstract class DrawableLoader : Loader
        {
            /// <summary>
            /// Callback function allowing to break loading of variable.
            /// </summary>
            public delegate bool LoadCallback();

            /// <summary>
            /// Returns geometrical information of a Drawable.
            /// It then can be passed into ExpressionDrawer.Draw().
            /// </summary>
            abstract public Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name);

            /// <summary>
            /// Loads debugged variable into ExpressionDrawer.IDrawable.
            /// It then can be passed into ExpressionDrawer.Draw()
            /// in order to draw a Drawable on Graphics surface.
            /// </summary>
            abstract public ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback);

            /// <summary>
            /// Returns MemoryReader.Converter object defining conversion
            /// from raw memory containing variables of various types,
            /// structures, arrays, etc. into an array of doubles.
            /// This object then can be used to convert blocks of memory
            /// while e.g. loading variables of a given type from a container.
            /// </summary>
            virtual public MemoryReader.Converter<double> GetMemoryConverter(MemoryReader mreader,
                                                                             Debugger debugger, // TODO - remove
                                                                             string name,
                                                                             string type)
            {
                return null;
            }

            /// <summary>
            /// Returns Drawable created from array of values generated by
            /// MemoryReader.Read() with MemoryReader.Converter.
            /// </summary>
            virtual public ExpressionDrawer.IDrawable DrawableFromMemory(MemoryReader.Converter<double> converter,
                                                                         double[] values, int offset)
            {
                return null;
            }
        }

        /// <summary>
        /// The base class of loaders which can load variables that can be drawn.
        /// It's more convenient to derive from this class than from DrawableLoader
        /// since it allows to define the exact ResultType.
        /// </summary>
        abstract class LoaderR<ResultType> : DrawableLoader
            where ResultType : ExpressionDrawer.IDrawable
        { }

        abstract class GeometryLoader<ResultType> : LoaderR<ResultType>
            where ResultType : ExpressionDrawer.IDrawable
        { }

        abstract class PointLoader : GeometryLoader<ExpressionDrawer.Point>
        {
            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                return LoadPoint(mreader, debugger, name, type);
            }
            
            virtual public ExpressionDrawer.Point LoadPoint(MemoryReader mreader, Debugger debugger, string name, string type)
            {
                ExpressionDrawer.Point result = null;
                if (mreader != null)
                    result = LoadPointMemory(mreader, debugger, name, type);
                if (result == null)
                    result = LoadPointParsed(debugger, name, type);
                return result;
            }
            abstract protected ExpressionDrawer.Point LoadPointParsed(Debugger debugger, string name, string type);
            abstract protected ExpressionDrawer.Point LoadPointMemory(MemoryReader mreader, Debugger debugger, string name, string type);

            public override ExpressionDrawer.IDrawable DrawableFromMemory(MemoryReader.Converter<double> converter,
                                                                          double[] values, int offset)
            {
                int valCount = converter.ValueCount();
                return valCount >= 2 ? new ExpressionDrawer.Point(values[offset], values[offset + 1])
                     : valCount == 1 ? new ExpressionDrawer.Point(values[offset], 0)
                     : null;
            }
        }

        abstract class BoxLoader : GeometryLoader<ExpressionDrawer.Box>
        {
            public override ExpressionDrawer.IDrawable DrawableFromMemory(MemoryReader.Converter<double> converter,
                                                                          double[] values, int offset)
            {
                int valCount = converter.ValueCount();
                if (valCount == 4)
                {
                    var mi = new ExpressionDrawer.Point(values[offset], values[offset + 1]);
                    var ma = new ExpressionDrawer.Point(values[offset + 2], values[offset + 3]);
                    return new ExpressionDrawer.Box(mi, ma);
                }
                else if (valCount == 2)
                {
                    var mi = new ExpressionDrawer.Point(values[offset], 0);
                    var ma = new ExpressionDrawer.Point(values[offset + 1], 0);
                    return new ExpressionDrawer.Box(mi, ma);
                }
                return null;
            }
        }

        abstract class SegmentLoader : GeometryLoader<ExpressionDrawer.Segment>
        {
            public override ExpressionDrawer.IDrawable DrawableFromMemory(MemoryReader.Converter<double> converter,
                                                                          double[] values, int offset)
            {
                int valCount = converter.ValueCount();
                if (valCount == 4)
                {
                    var p0 = new ExpressionDrawer.Point(values[offset], values[offset + 1]);
                    var p1 = new ExpressionDrawer.Point(values[offset + 2], values[offset + 3]);
                    return new ExpressionDrawer.Segment(p0, p1);
                }
                else if (valCount == 2)
                {
                    var p0 = new ExpressionDrawer.Point(values[offset], 0);
                    var p1 = new ExpressionDrawer.Point(values[offset + 1], 0);
                    return new ExpressionDrawer.Segment(p0, p1);
                }
                return null;
            }
        }

        abstract class NSphereLoader : GeometryLoader<ExpressionDrawer.NSphere>
        { }

        abstract class RangeLoader<ResultType> : GeometryLoader<ResultType>
            where ResultType : ExpressionDrawer.IDrawable
        { }

        abstract class PolygonLoader : GeometryLoader<ExpressionDrawer.Polygon>
        { }

        // Or ArrayPoint
        abstract class BXPoint : PointLoader
        {
            // memberArraySuffix has to start with '.'
            protected BXPoint(string memberArraySuffix, string coordType, Geometry.Traits traits)
            {
                this.memberArraySuffix = memberArraySuffix;
                this.coordType = coordType;
                this.traits = traits;
                this.count = Math.Min(traits.Dimension, 2);
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return traits;
            }

            protected override ExpressionDrawer.Point LoadPointParsed(Debugger debugger, string name, string type)
            {
                bool okx = true, oky = true;
                double x = 0, y = 0;
                string ptrName = name + memberArraySuffix;
                if (count >= 1)
                    okx = ExpressionParser.TryLoadDouble(debugger, ptrName + "[0]", out x);
                if (count >= 2)
                    oky = ExpressionParser.TryLoadDouble(debugger, ptrName + "[1]", out y);
                return Util.IsOk(okx, oky)
                     ? new ExpressionDrawer.Point(x, y)
                     : null;
            }

            protected override ExpressionDrawer.Point LoadPointMemory(MemoryReader mreader, Debugger debugger,
                                                                      string name, string type)
            {
                string ptrName = name + memberArraySuffix;
                VariableInfo info = new VariableInfo(debugger, ptrName + "[0]");
                if (! info.IsValid)
                    return null;

                double[] values = new double[count];
                if (mreader.ReadNumericArray(info.Address, info.Type, info.Size, values))
                {
                    if (count >= 2)
                        return new ExpressionDrawer.Point(values[0], values[1]);
                    else if (count == 1)
                        return new ExpressionDrawer.Point(values[0], 0);
                    else
                        return null;
                }

                return null;
            }

            public override MemoryReader.Converter<double> GetMemoryConverter(MemoryReader mreader,
                                                                              Debugger debugger, // TODO - remove
                                                                              string name, string type)
            {
                // TODO: byteSize and byteOffset could be created in LoaderCreator
                string ptrName = name + memberArraySuffix;
                string elemName = ptrName + "[0]";
                int elemSize = ExpressionParser.GetTypeSizeof(debugger, coordType);
                MemoryReader.Converter<double> arrayConverter
                    = mreader.GetNumericArrayConverter(coordType, elemSize, count);
                if (arrayConverter == null)
                    return null;
                int byteSize = (new ExpressionParser(debugger)).GetValueSizeof(name);
                if (byteSize == 0)
                    return null;
                long byteOffset = ExpressionParser.GetAddressDifference(debugger, name, elemName);
                if (ExpressionParser.IsInvalidOffset(byteSize, byteOffset))
                    return null;
                return new MemoryReader.StructConverter<double>(byteSize,
                            new MemoryReader.Member<double>(arrayConverter, (int)byteOffset));
            }

            string memberArraySuffix;
            string coordType;
            Geometry.Traits traits;
            int count;
        }

        class BGPoint : BXPoint
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Point; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::point")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 3)
                        return null;

                    string coordType = tparams[0];
                    int dimension = int.Parse(tparams[1]);
                    Geometry.CoordinateSystem cs = Geometry.CoordinateSystem.Cartesian;
                    Geometry.Unit unit = Geometry.Unit.None;
                    ParseCSAndUnit(tparams[2], out cs, out unit);

                    return new BGPoint(coordType, new Geometry.Traits(dimension, cs, unit));
                }
            }

            protected BGPoint(string coordType, Geometry.Traits traits)
                : base(".m_values", coordType, traits)
            { }

            protected static void ParseCSAndUnit(string cs_type, out Geometry.CoordinateSystem cs, out Geometry.Unit unit)
            {
                cs = Geometry.CoordinateSystem.Cartesian;
                unit = Geometry.Unit.None;

                if (cs_type == "boost::geometry::cs::cartesian")
                {
                    return;
                }

                string cs_base_type = Util.TypeId(cs_type);
                if (cs_base_type == "boost::geometry::cs::spherical")
                    cs = Geometry.CoordinateSystem.SphericalPolar;
                else if (cs_base_type == "boost::geometry::cs::spherical_equatorial")
                    cs = Geometry.CoordinateSystem.SphericalEquatorial;
                else if (cs_base_type == "boost::geometry::cs::geographic")
                    cs = Geometry.CoordinateSystem.Geographic;

                List<string> cs_tparams = Util.Tparams(cs_type);
                if (cs_tparams.Count >= 1)
                {
                    string u = cs_tparams[0];
                    if (u == "boost::geometry::radian")
                        unit = Geometry.Unit.Radian;
                    else if (u == "boost::geometry::degree")
                        unit = Geometry.Unit.Degree;
                }
            }
        }

        class BGPointXY : BGPoint
        {
            public new class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Point; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::d2::point_xy")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 2)
                        return null;

                    string coordType = tparams[0];
                    Geometry.CoordinateSystem cs = Geometry.CoordinateSystem.Cartesian;
                    Geometry.Unit unit = Geometry.Unit.None;
                    ParseCSAndUnit(tparams[1], out cs, out unit);

                    return new BGPointXY(coordType, new Geometry.Traits(2, cs, unit));
                }
            }

            public BGPointXY(string coordType, Geometry.Traits traits)
                : base(coordType, traits)
            {}
        }

        class BGBox : BoxLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Box; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::box")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string m_min_corner = name + ".m_min_corner";
                    string m_max_corner = name + ".m_max_corner";

                    string pointType = tparams[0];
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 m_min_corner,
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    int sizeOf = ExpressionParser.GetTypeSizeof(debugger, type);
                    if (ExpressionParser.IsInvalidSize(sizeOf))
                        return null;

                    long minDiff = ExpressionParser.GetAddressDifference(debugger, name, m_min_corner);
                    long maxDiff = ExpressionParser.GetAddressDifference(debugger, name, m_max_corner);
                    if (ExpressionParser.IsInvalidOffset(sizeOf, minDiff, maxDiff))
                        return null;

                    return new BGBox(pointLoader, pointType, sizeOf, minDiff, maxDiff);
                }
            }

            private BGBox(PointLoader pointLoader, string pointType, int sizeOf, long minDiff, long maxDiff)
            {
                this.pointLoader = pointLoader;
                this.pointType = pointType;
                this.sizeOf = sizeOf;
                this.minDiff = minDiff;
                this.maxDiff = maxDiff;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                string m_min_corner = name + ".m_min_corner";
                string m_max_corner = name + ".m_max_corner";

                Geometry.Point fp = pointLoader.LoadPoint(mreader, debugger, m_min_corner, pointType);
                Geometry.Point sp = pointLoader.LoadPoint(mreader, debugger, m_max_corner, pointType);

                return Util.IsOk(fp, sp)
                     ? new ExpressionDrawer.Box(fp, sp)
                     : null;
            }

            public override MemoryReader.Converter<double> GetMemoryConverter(MemoryReader mreader,
                                                                              Debugger debugger, // TODO - remove
                                                                              string name, string type)
            {
                string m_min_corner = name + ".m_min_corner";
                string m_max_corner = name + ".m_max_corner";

                MemoryReader.Converter<double> pointConverter = pointLoader.GetMemoryConverter(mreader, debugger, m_min_corner, pointType);
                if (pointConverter == null)
                    return null;

                return new MemoryReader.StructConverter<double>(sizeOf,
                            new MemoryReader.Member<double>(pointConverter, (int)minDiff),
                            new MemoryReader.Member<double>(pointConverter, (int)maxDiff));
            }

            PointLoader pointLoader;
            string pointType;
            long minDiff;
            long maxDiff;
            int sizeOf;
        }

        class BGSegment : SegmentLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Segment; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::segment")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string first = name + ".first";
                    string second = name + ".second";

                    string pointType = tparams[0];
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 first,
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    int sizeOf = ExpressionParser.GetTypeSizeof(debugger, type);
                    if (ExpressionParser.IsInvalidSize(sizeOf))
                        return null;

                    long firstDiff = ExpressionParser.GetAddressDifference(debugger, name, first);
                    long secondDiff = ExpressionParser.GetAddressDifference(debugger, name, second);
                    if (ExpressionParser.IsInvalidOffset(sizeOf, firstDiff, secondDiff))
                        return null;

                    return new BGSegment(pointLoader, pointType, sizeOf, firstDiff, secondDiff);
                }
            }

            protected BGSegment(PointLoader pointLoader, string pointType, int sizeOf, long firstDiff, long secondDiff)
            {
                this.pointLoader = pointLoader;
                this.pointType = pointType;
                this.sizeOf = sizeOf;
                this.firstDiff = firstDiff;
                this.secondDiff = secondDiff;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                string first = name + ".first";
                string second = name + ".second";

                Geometry.Point fp = pointLoader.LoadPoint(mreader, debugger, first, pointType);
                Geometry.Point sp = pointLoader.LoadPoint(mreader, debugger, second, pointType);

                return Util.IsOk(fp, sp)
                     ? new ExpressionDrawer.Segment(fp, sp)
                     : null;
            }

            public override MemoryReader.Converter<double> GetMemoryConverter(MemoryReader mreader,
                                                                              Debugger debugger, // TODO - remove
                                                                              string name, string type)
            {
                // NOTE: Because it can be created by derived class
                //   and these members can be set to invalid values
                //   e.g. BGReferringSegment
                if (ExpressionParser.IsInvalidSize(sizeOf)
                    || ExpressionParser.IsInvalidOffset(sizeOf, firstDiff, secondDiff))
                    return null;

                string first = name + ".first";
                MemoryReader.Converter<double> pointConverter = pointLoader.GetMemoryConverter(mreader, debugger, first, pointType);
                if (pointConverter == null)
                    return null;

                return new MemoryReader.StructConverter<double>(sizeOf,
                            new MemoryReader.Member<double>(pointConverter, (int)firstDiff),
                            new MemoryReader.Member<double>(pointConverter, (int)secondDiff));
            }

            PointLoader pointLoader;
            string pointType;
            long firstDiff;
            long secondDiff;
            int sizeOf;
        }

        class BGReferringSegment : BGSegment
        {
            public new class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Segment; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::referring_segment")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string first = name + ".first";
                    string pointType = tparams[0];
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 first,
                                                                 pointType) as PointLoader;

                    return new BGReferringSegment(pointLoader, pointType);
                }
            }

            private BGReferringSegment(PointLoader pointLoader, string pointType)
                : base(pointLoader, pointType, 0, -1, -1)
            { }
        }

        class BGNSphere : NSphereLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.NSphere; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::nsphere")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string m_center = name + ".m_center";

                    string pointType = tparams[0];
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 m_center,
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    return new BGNSphere(pointLoader, pointType);
                }
            }

            private BGNSphere(PointLoader pointLoader, string pointType)
            {
                this.pointLoader = pointLoader;
                this.pointType = pointType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                string m_center = name + ".m_center";
                string m_radius = name + ".m_radius";

                Geometry.Point center = pointLoader.LoadPoint(mreader, debugger,
                                                              m_center, pointType);
                double radius = 0;
                bool ok = ExpressionParser.TryLoadDouble(debugger, m_radius, out radius);

                return Util.IsOk(center, ok)
                     ? new ExpressionDrawer.NSphere(center, radius)
                     : null;
            }

            PointLoader pointLoader;
            string pointType;
        }

        abstract class PointRange<ResultType> : RangeLoader<ResultType>
            where ResultType : class
                             , ExpressionDrawer.IDrawable
                             , Geometry.IContainer<Geometry.Point>
                             , new()
        {
            protected ResultType LoadParsed(MemoryReader mreader, // should this be passed?
                                            Debugger debugger, string name, string type,
                                            string pointType,
                                            PointLoader pointLoader,
                                            ContainerLoader containerLoader,
                                            LoadCallback callback)
            {
                ResultType result = new ResultType();
                containerLoader.ForEachElement(debugger, name, delegate (string elName)
                {
                    ExpressionDrawer.Point p = pointLoader.LoadPoint(mreader, debugger, elName, pointType);
                    if (p == null)
                    {
                        result = null;
                        return false;
                    }
                    result.Add(p);

                    return callback();
                });
                return result;
            }

            protected ResultType LoadMemory(MemoryReader mreader,
                                            Debugger debugger,
                                            string name, string type,
                                            string pointName, string pointType,
                                            PointLoader pointLoader,
                                            ContainerLoader containerLoader,
                                            LoadCallback callback)
            {
                MemoryReader.Converter<double> pointConverter = pointLoader.GetMemoryConverter(
                    mreader, debugger, pointName, pointType);
                if (pointConverter == null)
                    return null;

                int dimension = pointConverter.ValueCount();
                ResultType result = new ResultType();
                if (containerLoader.ForEachMemoryBlock(mreader, debugger,
                        name, type, 0, pointConverter,
                        delegate (double[] values)
                        {
                            if (dimension == 0 || values.Length % dimension != 0)
                                return false;
                            int size = dimension > 0
                                        ? values.Length / dimension
                                        : 0;
                            for (int i = 0; i < size; ++i)
                            {
                                double x = dimension > 0 ? values[i * dimension] : 0;
                                double y = dimension > 1 ? values[i * dimension + 1] : 0;
                                ExpressionDrawer.Point p = new ExpressionDrawer.Point(x, y);
                                result.Add(p);
                            }

                            return callback();
                        }))
                {
                    return result;
                }

                return null;
            }
        }

        class BGRange<ResultType> : PointRange<ResultType>
            where ResultType : class
                             , ExpressionDrawer.IDrawable
                             , Geometry.IContainer<Geometry.Point>
                             , new()
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public delegate Loader DerivedConstructor(ContainerLoader containerLoader, string containerType,
                                                          PointLoader pointLoader, string pointType);

                public LoaderCreator(Kind kind, string id,
                                     int pointTIndex, int containerTIndex, int allocatorTIndex,
                                     DerivedConstructor derivedConstructor)
                {
                    this.kind = kind;
                    this.id = id;
                    this.pointTIndex = pointTIndex;
                    this.containerTIndex = containerTIndex;
                    this.allocatorTIndex = allocatorTIndex;
                    this.derivedConstructor = derivedConstructor;
                }
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return kind; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != this.id)
                        return null;

                    string pointType, containerType;
                    GetBGContainerInfo(type, pointTIndex, containerTIndex, allocatorTIndex,
                                       out pointType, out containerType);

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         containerType) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string pointName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out pointName, out dummyType);

                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 pointName,
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    return derivedConstructor(containerLoader, containerType, pointLoader, pointType);
                }

                Kind kind;
                string id;
                int pointTIndex;
                int containerTIndex;
                int allocatorTIndex;
                DerivedConstructor derivedConstructor;
            }

            protected BGRange(ContainerLoader containerLoader, string containerType,
                              PointLoader pointLoader, string pointType)
            {
                this.containerLoader = containerLoader;
                this.containerType = containerType;
                this.pointLoader = pointLoader;
                this.pointType = pointType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ResultType result = null;

                if (mreader != null)
                {
                    string pointName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out pointName, out dummyType);

                    result = LoadMemory(mreader, debugger, name, type,
                                        pointName, pointType, pointLoader,
                                        containerLoader, callback);
                }

                if (result == null)
                {
                    result = LoadParsed(mreader, debugger, name, type,
                                        pointType, pointLoader,
                                        containerLoader, callback);
                }

                return result;
            }

            ContainerLoader containerLoader;
            string containerType;
            PointLoader pointLoader;
            string pointType;
        }

        class BGLinestring : BGRange<ExpressionDrawer.Linestring>
        {
            public new class LoaderCreator : BGRange<ExpressionDrawer.Linestring>.LoaderCreator
            {
                public LoaderCreator()
                    : base(ExpressionLoader.Kind.Linestring,
                           "boost::geometry::model::linestring",
                           0, 1, 2,
                           delegate (ContainerLoader containerLoader, string containerType,
                                     PointLoader pointLoader, string pointType)
                                     {
                                         return new BGLinestring(containerLoader, containerType,
                                                                 pointLoader, pointType);
                                     })
                { }
            }

            private BGLinestring(ContainerLoader containerLoader, string containerType,
                                 PointLoader pointLoader, string pointType)
                : base(containerLoader, containerType, pointLoader, pointType)
            { }
        }

        class BGMultiLinestring : RangeLoader<ExpressionDrawer.MultiLinestring>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.MultiLinestring; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::multi_linestring")
                        return null;

                    string lsType, containerType;
                    GetBGContainerInfo(type, 0, 1, 2, out lsType, out containerType);

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         containerType) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string lsName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out lsName, out dummyType);

                    RangeLoader<ExpressionDrawer.Linestring>
                        lsLoader = loaders.FindByType(ExpressionLoader.Kind.Linestring,
                                                      lsName,
                                                      lsType) as RangeLoader<ExpressionDrawer.Linestring>;
                    if (lsLoader == null)
                        return null;

                    return new BGMultiLinestring(containerLoader, containerType, lsLoader, lsType);
                }
            }

            private BGMultiLinestring(ContainerLoader containerLoader,
                                      string containerType,
                                      RangeLoader<ExpressionDrawer.Linestring> lsLoader,
                                      string lsType)
            {
                this.containerLoader = containerLoader;
                this.containerType = containerType;
                this.lsLoader = lsLoader;
                this.lsType = lsType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return lsLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.MultiLinestring mls = new ExpressionDrawer.MultiLinestring();
                bool ok = containerLoader.ForEachElement(debugger, name, delegate (string elName)
                {
                    ExpressionDrawer.Linestring ls = lsLoader.Load(mreader, debugger,
                                                                   elName, lsType,
                                                                   callback) as ExpressionDrawer.Linestring;
                    if (ls == null)
                        return false;
                    mls.Add(ls);
                    //return callback();
                    return true;
                });
                return ok ? mls : null;
            }

            ContainerLoader containerLoader;
            string containerType;
            RangeLoader<ExpressionDrawer.Linestring> lsLoader;
            string lsType;
        }

        class BGRing : BGRange<ExpressionDrawer.Ring>
        {
            public new class LoaderCreator : BGRange<ExpressionDrawer.Ring>.LoaderCreator
            {
                public LoaderCreator()
                    : base(ExpressionLoader.Kind.Ring,
                           "boost::geometry::model::ring",
                           0, 3, 4,
                           delegate (ContainerLoader containerLoader, string containerType,
                                     PointLoader pointLoader, string pointType)
                                     {
                                         return new BGRing(containerLoader, containerType,
                                                           pointLoader, pointType);
                                     })
                { }
            }

            private BGRing(ContainerLoader containerLoader, string containerType,
                           PointLoader pointLoader, string pointType)
                : base(containerLoader, containerType, pointLoader, pointType)
            { }
        }

        class BGMultiPoint : BGRange<ExpressionDrawer.MultiPoint>
        {
            public new class LoaderCreator : BGRange<ExpressionDrawer.MultiPoint>.LoaderCreator
            {
                public LoaderCreator()
                    : base(ExpressionLoader.Kind.MultiPoint,
                           "boost::geometry::model::multi_point",
                           0, 1, 2,
                           delegate (ContainerLoader containerLoader, string containerType,
                                     PointLoader pointLoader, string pointType)
                                     {
                                         return new BGMultiPoint(containerLoader, containerType,
                                                                 pointLoader, pointType);
                                     })
                { }
            }

            private BGMultiPoint(ContainerLoader containerLoader, string containerType,
                                 PointLoader pointLoader, string pointType)
                : base(containerLoader, containerType, pointLoader, pointType)
            { }
        }

        class BGPolygon : PolygonLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Polygon; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::polygon")
                        return null;

                    string outerName = name + ".m_outer";
                    string innersName = name + ".m_inners";

                    Expression outerExpr = debugger.GetExpression(outerName);
                    Expression innersExpr = debugger.GetExpression(innersName);

                    string outerType = outerExpr.Type;
                    BGRing outerLoader = loaders.FindByType(ExpressionLoader.Kind.Ring,
                                                            outerName, outerType) as BGRing;
                    if (outerLoader == null)
                        return null;

                    string innersType = innersExpr.Type;
                    ContainerLoader innersLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                      innersName, innersType) as ContainerLoader;
                    if (innersLoader == null)
                        return null;

                    return new BGPolygon(outerLoader, outerType, innersLoader, innersType);
                }
            }

            // TODO: Should this be BGRing or a generic ring Loader?
            private BGPolygon(BGRing outerLoader, string outerType,
                              ContainerLoader innersLoader, string innersType)
            {
                this.outerLoader = outerLoader;
                this.outerType = outerType;
                this.innersLoader = innersLoader;
                this.innersType = innersType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return outerLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                string outerName = name + ".m_outer";
                string innersName = name + ".m_inners";

                ExpressionDrawer.Ring outer = outerLoader.Load(mreader, debugger,
                                                               outerName, outerType,
                                                               callback) as ExpressionDrawer.Ring;
                if (outer == null)
                    return null;

                List<Geometry.Ring> inners = new List<Geometry.Ring>();
                bool ok = innersLoader.ForEachElement(debugger, innersName, delegate (string elName)
                {
                    ExpressionDrawer.Ring inner = outerLoader.Load(mreader, debugger,
                                                                   elName, outerType,
                                                                   callback) as ExpressionDrawer.Ring;
                    if (inner == null)
                        return false;
                    inners.Add(inner);
                    //return callback();
                    return true;
                });

                return ok
                     ? new ExpressionDrawer.Polygon(outer, inners)
                     : null;
            }

            BGRing outerLoader;
            string outerType;
            ContainerLoader innersLoader;
            string innersType;
        }

        class BGMultiPolygon : RangeLoader<ExpressionDrawer.MultiPolygon>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.MultiPolygon; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::model::multi_polygon")
                        return null;

                    string polyType, containerType;
                    GetBGContainerInfo(type, 0, 1, 2, out polyType, out containerType);

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         containerType) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string polyName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out polyName, out dummyType);

                    PolygonLoader polyLoader = loaders.FindByType(ExpressionLoader.Kind.Polygon,
                                                                  polyName,
                                                                  polyType) as PolygonLoader;
                    if (polyLoader == null)
                        return null;

                    return new BGMultiPolygon(containerLoader, containerType,
                                              polyLoader, polyType);
                }
            }

            private BGMultiPolygon(ContainerLoader containerLoader,
                                   string containerType,
                                   PolygonLoader polyLoader,
                                   string polyType)
            {
                this.containerLoader = containerLoader;
                this.containerType = containerType;
                this.polyLoader = polyLoader;
                this.polyType = polyType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return polyLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.MultiPolygon mpoly = new ExpressionDrawer.MultiPolygon();
                bool ok = containerLoader.ForEachElement(debugger, name, delegate (string elName)
                {
                    ExpressionDrawer.Polygon poly = polyLoader.Load(mreader, debugger,
                                                                    elName, polyType,
                                                                    callback) as ExpressionDrawer.Polygon;
                    if (poly == null)
                        return false;
                    mpoly.Add(poly);
                    //return callback();
                    return true;
                });
                return ok ? mpoly : null;
            }

            ContainerLoader containerLoader;
            string containerType;
            PolygonLoader polyLoader;
            string polyType;
        }

        class BGBufferedRing : RangeLoader<ExpressionDrawer.Ring>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Ring; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::detail::buffer::buffered_ring")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string ringType = tparams[0];
                    RangeLoader<ExpressionDrawer.Ring>
                        ringLoader = loaders.FindByType(ExpressionLoader.Kind.Ring,
                                                        name,
                                                        ringType) as RangeLoader<ExpressionDrawer.Ring>;
                    if (ringLoader == null)
                        return null;

                    return new BGBufferedRing(ringLoader, ringType);
                }
            }

            private BGBufferedRing(RangeLoader<ExpressionDrawer.Ring> ringLoader,
                                   string ringType)
            {
                this.ringLoader = ringLoader;
                this.ringType = ringType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return ringLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                return ringLoader.Load(mreader, debugger, name, ringType, callback);
            }

            RangeLoader<ExpressionDrawer.Ring> ringLoader;
            string ringType;
        }

        // NOTE: There is no MultiRing concept so use MultiPolygon for now
        class BGBufferedRingCollection : RangeLoader<ExpressionDrawer.MultiPolygon>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.MultiPolygon; } // Or MultiGeometry
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::detail::buffer::buffered_ring_collection")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string ringType = tparams[0];
                    string containerType = StdContainerType("std::vector", ringType, "std::allocator");

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         containerType) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string ringName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out ringName, out dummyType);

                    RangeLoader<ExpressionDrawer.Ring>
                        ringLoader = loaders.FindByType(ExpressionLoader.Kind.Ring,
                                                        ringName,
                                                        ringType) as RangeLoader<ExpressionDrawer.Ring>;
                    if (ringLoader == null)
                        return null;

                    return new BGBufferedRingCollection(containerLoader, containerType,
                                                        ringLoader, ringType);
                }
            }

            private BGBufferedRingCollection(ContainerLoader containerLoader, string containerType,
                                             RangeLoader<ExpressionDrawer.Ring> ringLoader, string ringType)
            {
                this.containerLoader = containerLoader;
                this.containerType = containerType;
                this.ringLoader = ringLoader;
                this.ringType = ringType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return ringLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.MultiPolygon mpoly = new ExpressionDrawer.MultiPolygon();
                bool ok = containerLoader.ForEachElement(debugger, name, delegate (string elName)
                {
                    ExpressionDrawer.Ring ring = ringLoader.Load(mreader, debugger,
                                                                 elName, ringType,
                                                                 callback) as ExpressionDrawer.Ring;
                    if (ring == null)
                        return false;
                    mpoly.Add(new ExpressionDrawer.Polygon(ring));
                    //return callback();
                    return true;
                });
                return ok ? mpoly : null;
            }

            ContainerLoader containerLoader;
            string containerType;
            RangeLoader<ExpressionDrawer.Ring> ringLoader;
            string ringType;
        }

        // NOTE: Technically R-tree could be treated as a Container of Points or MultiPoint
        //       and displayed in a PlotWatch.
        // TODO: Consider this.

        class BGIRtree : GeometryLoader<ExpressionDrawer.DrawablesContainer>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.OtherGeometry; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::geometry::index::rtree")
                        return null;

                    try
                    {
                        return new BGIRtree(loaders, debugger, name, type, id);
                    }
                    catch(CreationException)
                    {
                        return null;
                    }
                }
            }

            private class CreationException : Exception
            { }

            private BGIRtree(Loaders loaders, Debugger debugger, string name, string type, string id)
            {
                string valueType;
                indexableLoader = IndexableLoader(loaders, debugger,
                                                  name, type,
                                                  out valueType,
                                                  out indexableMember,
                                                  out indexableType);
                if (indexableLoader == null)
                    throw new CreationException();

                // TODO: This is not fully correct.
                // The traits can technically be dynamic as in case of BVariant.
                // So they should be gathered from the actual data.
                // NOTE mreader = null
                traits = indexableLoader.GetTraits(null, debugger,
                                                   "(*((" + indexableType + "*)((void*)0)))");
                if (traits == null)
                    throw new CreationException();

                ExpressionParser exprParser = new ExpressionParser(debugger);

                string nodePtrName = RootNodePtr(name);
                string nodeVariantName = "*" + nodePtrName;
                string nodeVariantType = exprParser.GetValueType(nodeVariantName);
                if (Util.Empty(nodeVariantType))
                    throw new CreationException();

                if (!Util.Tparams(nodeVariantType, out leafType, out internalNodeType))
                    throw new CreationException();

                string leafElemsName;
                leafElementsLoader = ElementsLoader(loaders, debugger,
                                                    nodePtrName, leafType,
                                                    out leafElemsName, out leafElemsType);
                if (leafElementsLoader == null)
                    throw new CreationException();

                string internalNodeElemsName;
                internalNodeElementsLoader = ElementsLoader(loaders, debugger,
                                                            nodePtrName, internalNodeType,
                                                            out internalNodeElemsName, out internalNodeElemsType);
                if (internalNodeElementsLoader == null)
                    throw new CreationException();

                // For Memory Loading

                nodePtrType = exprParser.GetValueType(nodePtrName);
                nodePtrSizeOf = exprParser.GetTypeSizeof(nodePtrType);

                leafElemsDiff = exprParser.GetAddressDifference(nodeVariantName,
                                                                NodeElements(nodePtrName, leafType));
                internalNodeElemsDiff = exprParser.GetAddressDifference(nodeVariantName,
                                                                        NodeElements(nodePtrName, internalNodeType));
                string leafElemName;
                leafElementsLoader.ElementInfo(leafElemsName, leafElemsType,
                                               out leafElemName, out leafElemType);
                string internalNodeElemName;
                internalNodeElementsLoader.ElementInfo(internalNodeElemsName, internalNodeElemsType,
                                                       out internalNodeElemName, out internalNodeElemType);

                indexableDiff = exprParser.GetAddressDifference(leafElemName,
                                                                leafElemName + indexableMember);
                nodePtrDiff = exprParser.GetAddressDifference(internalNodeElemName,
                                                              internalNodeElemName + ".second");

                string whichName = "(" + nodeVariantName + ").which_";
                whichType = exprParser.GetValueType(whichName);
                whichSizeOf = exprParser.GetTypeSizeof(whichType);
                whichDiff = exprParser.GetAddressDifference(nodeVariantName,
                                                            whichName);

                nodeVariantSizeof = exprParser.GetValueSizeof(nodeVariantName);
                nodePtrPairSizeof = exprParser.GetValueSizeof(internalNodeElemName);
                valueSizeof = exprParser.GetValueSizeof(leafElemName);
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return traits;
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.DrawablesContainer result = null;

                int size = LoadSize(debugger, name);
                if (size <= 0)
                    return null;

                string nodePtrName = RootNodePtr(name);

                LoadMemory(mreader, debugger, nodePtrName, out result, callback);

                if (result == null)
                {
                    ExpressionDrawer.DrawablesContainer res = new ExpressionDrawer.DrawablesContainer();
                    if (LoadParsedRecursive(mreader, debugger,
                                            nodePtrName,
                                            res,
                                            callback))
                    {
                        result = res;
                    }
                }

                return result;
            }

            private void LoadMemory(MemoryReader mreader, Debugger debugger,
                                    string nodePtrName,
                                    out ExpressionDrawer.DrawablesContainer result,
                                    LoadCallback callback)
            {
                result = null;

                string nodeVariantName = "*" + nodePtrName;
                string whichName = "(" + nodeVariantName + ").which_";

                if (mreader == null)
                    return;

                if (ExpressionParser.IsInvalidAddressDifference(leafElemsDiff)
                    || ExpressionParser.IsInvalidAddressDifference(internalNodeElemsDiff)
                    || ExpressionParser.IsInvalidAddressDifference(indexableDiff)
                    || ExpressionParser.IsInvalidAddressDifference(nodePtrDiff)
                    || ExpressionParser.IsInvalidAddressDifference(whichDiff))
                    return;

                ulong rootAddr = ExpressionParser.GetValueAddress(debugger, nodeVariantName);
                if (rootAddr == 0)
                    return;

                MemoryReader.Converter<int> whichConverter = mreader.GetValueConverter<int>(whichType, whichSizeOf);
                if (whichConverter == null)
                    return;

                if (nodeVariantSizeof <= 0)
                    return;

                MemoryReader.StructConverter<int> nodeVariantConverter =
                    new MemoryReader.StructConverter<int>(
                        nodeVariantSizeof,
                        new MemoryReader.Member<int>(whichConverter, (int)whichDiff));

                MemoryReader.ValueConverter<ulong> nodePtrConverter =
                    mreader.GetPointerConverter(nodePtrType, nodePtrSizeOf);
                if (nodePtrConverter == null)
                    return;

                MemoryReader.Converter<ulong> nodePtrPairConverter =
                    new MemoryReader.StructConverter<ulong>(
                        nodePtrPairSizeof,
                        new MemoryReader.Member<ulong>(nodePtrConverter, (int)nodePtrDiff));

                string leafElemsName = NodeElements(nodePtrName, leafType);
                string leafElemName, leafElemType;
                leafElementsLoader.ElementInfo(leafElemsName, leafElemsType,
                                                out leafElemName, out leafElemType);
                string indexableName = leafElemName + indexableMember;

                string internalNodeElemsName = NodeElements(nodePtrName, internalNodeType);
                string internalNodeElemName;
                internalNodeElementsLoader.ElementInfo(internalNodeElemsName, internalNodeElemsType,
                                                        out internalNodeElemName, out internalNodeElemType);

                MemoryReader.Converter<double> indexableConverter =
                    indexableLoader.GetMemoryConverter(mreader, debugger, indexableName, indexableType);
                if (indexableConverter == null)
                    return;

                MemoryReader.Converter<double> valueConverter = indexableConverter;

                if (indexableMember != "" /*&& indexableDiff > 0*/)
                {
                    valueConverter = new MemoryReader.StructConverter<double>(
                        valueSizeof,
                        new MemoryReader.Member<double>(indexableConverter, (int)indexableDiff));
                }

                ExpressionDrawer.DrawablesContainer res = new ExpressionDrawer.DrawablesContainer();
                if (LoadMemoryRecursive(mreader, debugger,
                                        rootAddr,
                                        nodeVariantConverter,
                                        valueConverter,
                                        nodePtrPairConverter,
                                        res,
                                        callback))
                {
                    result = res;                      
                }
            }

            private bool LoadMemoryRecursive(MemoryReader mreader, Debugger debugger,
                                             ulong nodeAddr,
                                             MemoryReader.StructConverter<int> nodeVariantConverter,
                                             MemoryReader.Converter<double> valueConverter,
                                             MemoryReader.Converter<ulong> nodePtrPairConverter,
                                             ExpressionDrawer.DrawablesContainer result,
                                             LoadCallback callback)
            {
                int[] which = new int[1];
                if (!mreader.Read(nodeAddr, which, nodeVariantConverter))
                    return false;

                if (which[0] == 0) // leaf
                {
                    ulong leafElemsAddress = nodeAddr + (ulong)leafElemsDiff;
                    if (!leafElementsLoader.ForEachMemoryBlock(mreader, debugger,
                            "", "",
                            leafElemsAddress,
                            valueConverter,
                            delegate (double[] values)
                            {
                                if (indexableLoader is PointLoader)
                                {
                                    if (values.Length % 2 != 0)
                                        return false;
                                    for (int i = 0; i < values.Length / 2; ++i)
                                        result.Add(new ExpressionDrawer.Point(values[i * 2],
                                                                              values[i * 2 + 1]));
                                }
                                else if (indexableLoader is BoxLoader)
                                {
                                    if (values.Length % 4 != 0)
                                        return false;
                                    for (int i = 0; i < values.Length / 4; ++i)
                                        result.Add(new ExpressionDrawer.Box(
                                            new ExpressionDrawer.Point(values[i * 4],
                                                                       values[i * 4 + 1]),
                                            new ExpressionDrawer.Point(values[i * 4 + 2],
                                                                       values[i * 4 + 3])));
                                }
                                else if (indexableLoader is SegmentLoader)
                                {
                                    if (values.Length % 4 != 0)
                                        return false;
                                    for (int i = 0; i < values.Length / 4; ++i)
                                        result.Add(new ExpressionDrawer.Segment(
                                            new ExpressionDrawer.Point(values[i * 4],
                                                                       values[i * 4 + 1]),
                                            new ExpressionDrawer.Point(values[i * 4 + 2],
                                                                       values[i * 4 + 3])));
                                }
                                else
                                    return false;
                                return true;
                            }))
                        return false;
                }
                else if (which[0] == 1) // internal node
                {
                    ulong internalNodeElemsAddress = nodeAddr + (ulong)internalNodeElemsDiff;
                    if (!internalNodeElementsLoader.ForEachMemoryBlock(mreader, debugger,
                            "", "",
                            internalNodeElemsAddress, nodePtrPairConverter,
                            delegate (ulong[] ptrs)
                            {
                                foreach (ulong addr in ptrs)
                                {
                                    if (!LoadMemoryRecursive(mreader, debugger,
                                                             addr,
                                                             nodeVariantConverter,
                                                             valueConverter,
                                                             nodePtrPairConverter,
                                                             result,
                                                             callback))
                                    {
                                        return false;
                                    }
                                }
                                return true;
                            }))
                        return false;
                }
                else
                    return false;

                return callback();
            }
            
            private bool LoadParsedRecursive(MemoryReader mreader, Debugger debugger,
                                             string nodePtrName,
                                             ExpressionDrawer.DrawablesContainer result,
                                             LoadCallback callback)
            {
                bool isLeaf;
                if (!IsLeaf(debugger, nodePtrName, out isLeaf))
                    return false;

                string nodeType = leafType;
                ContainerLoader elementsLoader = leafElementsLoader;
                if (!isLeaf)
                {
                    nodeType = internalNodeType;
                    elementsLoader = internalNodeElementsLoader;
                }
                string elementsName = NodeElements(nodePtrName, nodeType);

                bool ok = elementsLoader.ForEachElement(debugger, elementsName, delegate (string elName)
                {
                    if (isLeaf)
                    {
                        ExpressionDrawer.IDrawable indexable = indexableLoader.Load(
                            mreader, debugger,
                            elName + indexableMember, indexableType,
                            callback); // rather dummy callback

                        if (indexable == null)
                            return false;

                        result.Add(indexable);
                    }
                    else
                    {
                        string nextNodePtrName = elName + ".second";
                        if (!LoadParsedRecursive(mreader, debugger,
                                                 nextNodePtrName,
                                                 result,
                                                 callback))
                            return false;
                    }

                    return callback();
                });

                return ok;
            }

            static DrawableLoader IndexableLoader(Loaders loaders, Debugger debugger,
                                                  string name, string type,
                                                  out string valueType,
                                                  out string indexableMember,
                                                  out string indexableType)
            {
                indexableMember = "";
                indexableType = "";

                if (!Util.Tparams(type, out valueType))
                    return null;

                // NOTE: Casting the address 0 is not correct because in some cases
                // addresses can be calculated on loader creation and address 0 is
                // currently reserved as invalid.
                // So below the address of the R-tree object is used.
                ulong address = ExpressionParser.GetValueAddress(debugger, name);
                string addressStr = address.ToString();

                string valueId = Util.TypeId(valueType);
                DrawableLoader indexableLoader = null;
                indexableMember = "";
                indexableType = valueType;
                if (valueId == "std::pair" || valueId == "std::tuple" || valueId == "boost::tuple")
                {
                    string firstType;
                    if (!Util.Tparams(valueType, out firstType))
                        return null;

                    
                    indexableLoader = loaders.FindByType(OnlyIndexables,
                                                         "(*((" + firstType + "*)" + addressStr + "))",
                                                         firstType) as DrawableLoader;

                    // The first type of pair/tuple is an Indexable
                    // so assume the pair/tuple is not a Geometry itself
                    if (indexableLoader != null)
                    {
                        indexableType = firstType;
                        if (valueId == "std::pair")
                            indexableMember = ".first";
                        else if (valueId == "std::tuple")
                            indexableMember = "._Myfirst._Val";
                        else // boost::tuple
                            indexableMember = ".head";
                    }
                }

                if (indexableLoader == null)
                {
                    // NOTE: Casting the address 0 is not correct because in some cases
                    // addresses can be calculated on loader creation and address 0 is
                    // currently reserved as invalid.
                    indexableLoader = loaders.FindByType(OnlyIndexables,
                                                         "(*((" + indexableType + "*)" + addressStr + "))",
                                                         indexableType) as DrawableLoader;
                }

                return indexableLoader;
            }

            static ContainerLoader ElementsLoader(Loaders loaders, Debugger debugger,
                                                  string nodePtrName, string castedNodeType,
                                                  out string elementsName, out string containerType)
            {
                elementsName = NodeElements(nodePtrName, castedNodeType);
                containerType = "";

                Expression expr = debugger.GetExpression(elementsName);
                if (!expr.IsValidValue)
                    return null;

                elementsName = expr.Name;
                containerType = expr.Type;

                return loaders.FindByType(ExpressionLoader.Kind.Container,
                                          expr.Name, expr.Type) as ContainerLoader;
            }

            int LoadSize(Debugger debugger, string name)
            {
                return ExpressionParser.LoadSize(debugger, name + ".m_members.values_count");
            }

            static string RootNodePtr(string name)
            {
                return name + ".m_members.root";
            }

            static string NodeElements(string nodePtrName, string castedNodeType)
            {
                return "((" + castedNodeType + "*)" + nodePtrName + "->storage_.data_.buf)->elements";
            }

            static bool IsLeaf(Debugger debugger, string nodePtrName, out bool result)
            {
                result = false;

                int which = 0;
                if (!ExpressionParser.TryLoadInt(debugger, nodePtrName + "->which_", out which))
                    return false;

                result = (which == 0);
                return true;
            }

            ContainerLoader leafElementsLoader;
            ContainerLoader internalNodeElementsLoader;
            DrawableLoader indexableLoader;

            Geometry.Traits traits;

            string leafType;
            string internalNodeType;
            string indexableMember;
            string indexableType;
            string nodePtrType; // pointer to root or a node in internal node
            string leafElemsType; // type of container of values
            string internalNodeElemsType; // type of container of ptr_pair<box, node_ptr>
            string leafElemType; // type of value
            string internalNodeElemType; // type of ptr_pair<box, node_ptr>
            string whichType; // type of which_ member

            long leafElemsDiff; // offset of container of values in variant node which is a leaf
            long internalNodeElemsDiff; // offset of container of ptr_pair<box, node_ptr> in variant node which is internal node
            long indexableDiff; // offset of indexable in value
            long nodePtrDiff; // offset of node_ptr in ptr_pair<box, node_ptr>
            long whichDiff; // offset of which_ member of variant node

            int nodePtrSizeOf; // size of pointer (4 or 8)
            int whichSizeOf; // size of which_ member            
            int nodeVariantSizeof; // size of variant node
            int nodePtrPairSizeof; // size of ptr_pair<box, node_ptr>
            int valueSizeof; // size of value
        }

        class BPPoint : BXPoint
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Point; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::polygon::point_data")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;
                    string coordType = tparams[0];

                    return new BPPoint(coordType);
                }
            }

            private BPPoint(string coordType)
                : base(".coords_",
                       coordType,
                       new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None))
            { }
        }

        class BPSegment : SegmentLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Segment; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    return id == "boost::polygon::segment_data"
                         ? new BPSegment()
                         : null;
                }
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                bool okx0 = ExpressionParser.TryLoadDouble(debugger, name + ".points_[0].coords_[0]", out x0);
                bool oky0 = ExpressionParser.TryLoadDouble(debugger, name + ".points_[0].coords_[1]", out y0);
                bool okx1 = ExpressionParser.TryLoadDouble(debugger, name + ".points_[1].coords_[0]", out x1);
                bool oky1 = ExpressionParser.TryLoadDouble(debugger, name + ".points_[1].coords_[1]", out y1);

                if (! Util.IsOk(okx0, oky0, okx1, oky1))
                    return null;

                Geometry.Point first_p = new Geometry.Point(x0, y0);
                Geometry.Point second_p = new Geometry.Point(x1, y1);

                return new ExpressionDrawer.Segment(first_p, second_p);
            }
        }

        class BPBox : BoxLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Box; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    return id == "boost::polygon::rectangle_data"
                         ? new BPBox()
                         : null;
                }
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback) // dummy callback
            {
                double xl = 0, xh = 0, yl = 0, yh = 0;
                bool okxl = ExpressionParser.TryLoadDouble(debugger, name + ".ranges_[0].coords_[0]", out xl);
                bool okxh = ExpressionParser.TryLoadDouble(debugger, name + ".ranges_[0].coords_[1]", out xh);
                bool okyl = ExpressionParser.TryLoadDouble(debugger, name + ".ranges_[1].coords_[0]", out yl);
                bool okyh = ExpressionParser.TryLoadDouble(debugger, name + ".ranges_[1].coords_[1]", out yh);

                if (! Util.IsOk(okxl, okxh, okyl, okyh))
                    return null;

                Geometry.Point first_p = new Geometry.Point(xl, yl);
                Geometry.Point second_p = new Geometry.Point(xh, yh);

                return new ExpressionDrawer.Box(first_p, second_p);
            }
        }

        class BPRing : PointRange<ExpressionDrawer.Ring>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Ring; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::polygon::polygon_data")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string pointType = Util.TemplateType("boost::polygon::point_data", tparams[0]);
                    string containerType = StdContainerType("std::vector", pointType, "std::allocator");

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         containerType) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string pointName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out pointName, out dummyType);

                    BPPoint pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                             pointName,
                                                             pointType) as BPPoint;
                    if (pointLoader == null)
                        return null;

                    return new BPRing(containerLoader, containerType, pointLoader, pointType);
                }
            }

            private BPRing(ContainerLoader containerLoader, string containerType,
                           BPPoint pointLoader, string pointType)
            {
                this.containerLoader = containerLoader;
                this.containerType = containerType;
                this.pointLoader = pointLoader;
                this.pointType = pointType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.Ring result = null;

                string containerName = name + ".coords_";

                if (mreader != null)
                {
                    string pointName, dummyType;
                    containerLoader.ElementInfo(name, containerType, out pointName, out dummyType);

                    result = LoadMemory(mreader, debugger,
                                        containerName, type,
                                        pointName, pointType, pointLoader,
                                        containerLoader, callback);
                }

                if (result == null)
                {
                    result = LoadParsed(mreader, debugger, containerName, type,
                                        pointType, pointLoader, containerLoader,
                                        callback);
                }

                return result;
            }

            ContainerLoader containerLoader;
            string containerType;
            BPPoint pointLoader;
            string pointType;
        }

        class BPPolygon : PolygonLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Polygon; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::polygon::polygon_with_holes_data")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string polygonType = Util.TemplateType("boost::polygon::polygon_data", tparams[0]);
                    string containerType = Util.TemplateType("std::list",
                                                polygonType,
                                                Util.TemplateType("std::allocator",
                                                    polygonType));

                    string member_self_ = name + ".self_";
                    string member_holes_ = name + ".holes_";

                    BPRing outerLoader = loaders.FindByType(ExpressionLoader.Kind.Ring,
                                                            member_self_,
                                                            polygonType) as BPRing;
                    if (outerLoader == null)
                        return null;

                    ContainerLoader holesLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                     member_holes_,
                                                                     containerType) as ContainerLoader;
                    if (holesLoader == null)
                        return null;

                    return new BPPolygon(outerLoader, polygonType, holesLoader, containerType);
                }
            }

            private BPPolygon(BPRing outerLoader, string polygonType,
                              ContainerLoader holesLoader, string containerType)
            {
                this.outerLoader = outerLoader;
                this.polygonType = polygonType;
                this.holesLoader = holesLoader;
                this.containerType = containerType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                string member_self_ = name + ".self_";
                string member_holes_ = name + ".holes_";

                ExpressionDrawer.Ring outer = outerLoader.Load(mreader, debugger,
                                                               member_self_, polygonType,
                                                               callback) as ExpressionDrawer.Ring;
                if (outer == null)
                    return null;

                List<Geometry.Ring> holes = new List<Geometry.Ring>();
                bool ok = holesLoader.ForEachElement(debugger, member_holes_, delegate (string elName)
                {
                    ExpressionDrawer.Ring hole = outerLoader.Load(mreader, debugger,
                                                                  elName, polygonType,
                                                                  callback) as ExpressionDrawer.Ring;
                    if (hole == null)
                        return false;
                    holes.Add(hole);
                    return true;
                });

                return ok
                     ? new ExpressionDrawer.Polygon(outer, holes)
                     : null;
            }

            BPRing outerLoader;
            string polygonType;
            ContainerLoader holesLoader;
            string containerType;
        }

        class BVariant : DrawableLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Variant; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "boost::variant")
                        return null;

                    List<string> tparams = Util.Tparams(type);

                    DrawableLoader[] drawableLoaders = new DrawableLoader[tparams.Count];
                    for (int i = 0; i < tparams.Count; ++i)
                    {
                        string storedType = tparams[i];
                        string storedName = "(*(" + storedType + "*)" + name + ".storage_.data_.buf)";

                        drawableLoaders[i] = loaders.FindByType(AllDrawables, storedName, storedType)
                                                    as DrawableLoader;
                    }

                    return new BVariant(tparams, drawableLoaders);
                }
            }

            private BVariant(List<string> tparams, DrawableLoader[] loaders)
            {
                this.tparams = tparams;
                this.loaders = loaders;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                int which = 0;
                if (!ExpressionParser.TryLoadInt(debugger, name + ".which_", out which))
                    return null;

                if (which < 0 || tparams.Count <= which || tparams.Count != loaders.Length)
                    return null;

                if (loaders[which] == null)
                    return null;

                return loaders[which].GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                int which = 0;
                if (!ExpressionParser.TryLoadInt(debugger, name + ".which_", out which))
                    return null;

                if (which < 0 || tparams.Count <= which || tparams.Count != loaders.Length)
                    return null;

                if (loaders[which] == null)
                    return null;

                string storedType = tparams[which];
                string storedName = "(*(" + storedType + "*)" + name + ".storage_.data_.buf)";

                return loaders[which].Load(mreader, debugger, storedName, storedType,
                                           callback);
            }

            List<string> tparams;
            DrawableLoader[] loaders;
        }

        class StdPairPoint : PointLoader
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Point; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "std::pair")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 2)
                        return null;

                    return new StdPairPoint(tparams[0], tparams[1]);
                }
            }

            private StdPairPoint(string firstType, string secondType)
            {
                this.firstType = firstType;
                this.secondType = secondType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return new Geometry.Traits(2, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);
            }

            protected override ExpressionDrawer.Point LoadPointParsed(Debugger debugger, string name, string type)
            {
                double x = 0, y = 0;
                bool okx = ExpressionParser.TryLoadDouble(debugger, name + ".first", out x);
                bool oky = ExpressionParser.TryLoadDouble(debugger, name + ".second", out y);
                return Util.IsOk(okx, oky)
                     ? new ExpressionDrawer.Point(x, y)
                     : null;
            }

            protected override ExpressionDrawer.Point LoadPointMemory(MemoryReader mreader, Debugger debugger,
                                                                      string name, string type)
            {
                MemoryReader.Converter<double> converter = GetMemoryConverter(mreader, debugger, name, type);
                if (converter == null)
                    return null;

                if (converter.ValueCount() != 2)
                    throw new ArgumentOutOfRangeException("converter.ValueCount()");

                ulong address = ExpressionParser.GetValueAddress(debugger, name);
                if (address == 0)
                    return null;

                double[] values = new double[2];
                if (mreader.Read(address, values, converter))
                {
                    return new ExpressionDrawer.Point(values[0], values[1]);
                }

                return null;
            }

            public override MemoryReader.Converter<double> GetMemoryConverter(MemoryReader mreader,
                                                                              Debugger debugger, // TODO - remove
                                                                              string name, string type)
            {
                string first = name + ".first";
                string second = name + ".second";
                long firstOffset = ExpressionParser.GetAddressDifference(debugger, name, first);
                long secondOffset = ExpressionParser.GetAddressDifference(debugger, name, second);
                if (ExpressionParser.IsInvalidAddressDifference(firstOffset)
                 || ExpressionParser.IsInvalidAddressDifference(secondOffset))
                    return null;
                int firstSize = ExpressionParser.GetTypeSizeof(debugger, firstType);
                int secondSize = ExpressionParser.GetTypeSizeof(debugger, secondType);
                if (firstSize == 0 || secondSize == 0)
                    return null;
                MemoryReader.ValueConverter<double> firstConverter = mreader.GetNumericConverter(firstType, firstSize);
                MemoryReader.ValueConverter<double> secondConverter = mreader.GetNumericConverter(secondType, secondSize);
                if (firstConverter == null || secondConverter == null)
                    return null;
                int sizeOfPair = ExpressionParser.GetTypeSizeof(debugger, type);
                if (sizeOfPair == 0)
                    return null;
                return new MemoryReader.StructConverter<double>(
                            sizeOfPair,
                            new MemoryReader.Member<double>(firstConverter, (int)firstOffset),
                            new MemoryReader.Member<double>(secondConverter, (int)secondOffset));
            }

            string firstType;
            string secondType;
        }

        class StdComplexPoint : BXPoint
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Point; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != "std::complex")
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    return new StdComplexPoint(tparams[0]);
                }
            }

            private StdComplexPoint(string coordType)
                : base("._Val", coordType,
                       new Geometry.Traits(2, Geometry.CoordinateSystem.Complex, Geometry.Unit.None))
            { }
        }

        class BGTurn : GeometryLoader<ExpressionDrawer.Turn>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public LoaderCreator(string id)
                {
                    this.id = id;
                }

                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.Turn; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    if (id != this.id)
                        return null;

                    List<string> tparams = Util.Tparams(type);
                    if (tparams.Count < 1)
                        return null;

                    string pointType = tparams[0];
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 name + ".point",
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    return new BGTurn(pointLoader, pointType);
                }

                string id;
            }

            private BGTurn(PointLoader pointLoader, string pointType)
            {
                this.pointLoader = pointLoader;
                this.pointType = pointType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            // rather dummy callback
                                                            LoadCallback callback)
            {
                ExpressionDrawer.Point p = pointLoader.Load(mreader, debugger,
                                                            name + ".point", pointType,
                                                            // rather dummy callback
                                                            callback) as ExpressionDrawer.Point;
                if (p == null)
                    return null;

                char method = '?';
                Expression expr_method = debugger.GetExpression(name + ".method");
                if (expr_method.IsValidValue)
                    method = MethodChar(expr_method.Value);

                char op0 = '?';
                Expression expr_op0 = debugger.GetExpression(name + ".operations[0].operation");
                if (expr_op0.IsValidValue)
                    op0 = OperationChar(expr_op0.Value);

                char op1 = '?';
                Expression expr_op1 = debugger.GetExpression(name + ".operations[1].operation");
                if (expr_op1.IsValidValue)
                    op1 = OperationChar(expr_op1.Value);

                return new ExpressionDrawer.Turn(p, method, op0, op1);
            }

            private static char MethodChar(string method)
            {
                switch (method)
                {
                    case "method_none": return '-';
                    case "method_disjoint": return 'd';
                    case "method_crosses": return 'i';
                    case "method_touch": return 't';
                    case "method_touch_interior": return 'm';
                    case "method_collinear": return 'c';
                    case "method_equal": return 'e';
                    case "method_error": return '!';
                    default: return '?';
                }
            }

            private static char OperationChar(string operation)
            {
                switch (operation)
                {
                    case "operation_none": return '-';
                    case "operation_union": return 'u';
                    case "operation_intersection": return 'i';
                    case "operation_blocked": return 'x';
                    case "operation_continue": return 'c';
                    case "operation_opposite": return 'o';
                    default: return '?';
                }
            }

            PointLoader pointLoader;
            string pointType;            
        }

        class BGTurnContainer : GeometryLoader<ExpressionDrawer.TurnsContainer>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.TurnsContainer; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    // Any type/id, match all containers storing Turns

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         type) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string turnName, turnType;
                    containerLoader.ElementInfo(name, type, out turnName, out turnType);

                    // WARNING: Potentially recursive call, search for Turns only
                    BGTurn turnLoader = loaders.FindByType(ExpressionLoader.Kind.Turn,
                                                           turnName,
                                                           turnType) as BGTurn;
                    if (turnLoader == null)
                        return null;

                    return new BGTurnContainer(containerLoader, turnLoader, turnType);
                }
            }

            private BGTurnContainer(ContainerLoader containerLoader, BGTurn turnLoader, string turnType)
            {
                this.containerLoader = containerLoader;
                this.turnLoader = turnLoader;
                this.turnType = turnType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return turnLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                List<ExpressionDrawer.Turn> turns = new List<ExpressionDrawer.Turn>();
                bool ok = containerLoader.ForEachElement(debugger, name, delegate (string elName)
                {
                    ExpressionDrawer.Turn turn = turnLoader.Load(mreader, debugger,
                                                                 elName, turnType,
                                                                 // rather dummy callback
                                                                 callback) as ExpressionDrawer.Turn;
                    if (turn == null)
                        return false;
                    turns.Add(turn);
                    return callback();
                });
                return ok
                     ? new ExpressionDrawer.TurnsContainer(turns)
                     : null;
            }

            ContainerLoader containerLoader;
            BGTurn turnLoader;
            string turnType;
        }

        // TODO: This implementation is very similar to any MultiGeometry,
        //   including User-Defined ones, so if possible unify all of them
        class GeometryContainer : GeometryLoader<ExpressionDrawer.DrawablesContainer>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.GeometriesContainer; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    // Any type/id, match all containers storing Geometries

                    // TODO: This may match container of Turns instead of BGTurnContainer

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container, name, type) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string geometryName, geometryType;
                    containerLoader.ElementInfo(name, type, out geometryName, out geometryType);

                    // WARNING: Potentially recursive call, search for Geometries only,
                    //   GeometryContainer cannot be treated as a Geometry in GeometryKindConstraint
                    DrawableLoader geometryLoader = loaders.FindByType(OnlyGeometries,
                                                                       geometryName,
                                                                       geometryType) as DrawableLoader;
                    if (geometryLoader == null)
                        return null;

                    return new GeometryContainer(containerLoader, geometryLoader, geometryType);
                }
            }

            private GeometryContainer(ContainerLoader containerLoader,
                                      DrawableLoader geometryLoader,
                                      string geometryType)
            {
                this.containerLoader = containerLoader;
                this.geometryLoader = geometryLoader;
                this.geometryType = geometryType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return geometryLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.DrawablesContainer result = null;

                if  (mreader != null)
                {
                    string geometryName, dummyType;
                    containerLoader.ElementInfo(name, type, out geometryName, out dummyType);

                    result = LoadMemory(mreader, debugger,
                                        name, type,
                                        geometryName,
                                        callback);
                }

                if (result == null)
                {
                    result = LoadParsed(mreader, debugger, name, type, callback);
                }

                return result;
            }

            private ExpressionDrawer.DrawablesContainer LoadMemory(MemoryReader mreader, Debugger debugger,
                                                                   string name, string type,
                                                                   string geometryName, 
                                                                   LoadCallback callback)
            {
                MemoryReader.Converter<double> geometryConverter = geometryLoader.GetMemoryConverter(
                    mreader, debugger, geometryName, geometryType);

                if (geometryConverter == null)
                    return null;

                ExpressionDrawer.DrawablesContainer result = new ExpressionDrawer.DrawablesContainer();
                if (containerLoader.ForEachMemoryBlock(mreader, debugger,
                        name, type, 0, geometryConverter,
                        delegate (double[] values)
                        {
                            int valCount = geometryConverter.ValueCount();
                            if (valCount == 0 || values.Length % valCount != 0)
                                return false;
                            int geometriesCount = values.Length / valCount;

                            for (int i = 0; i < geometriesCount; ++i)
                            {
                                ExpressionDrawer.IDrawable d = geometryLoader.DrawableFromMemory(
                                    geometryConverter, values, i * valCount);

                                if (d == null)
                                    return false;

                                result.Add(d);
                            }

                            return callback();
                        }))
                {
                    return result;
                }

                return null;
            }

            private ExpressionDrawer.DrawablesContainer LoadParsed(MemoryReader mreader, Debugger debugger,
                                                                   string name, string type,
                                                                   LoadCallback callback)
            {
                ExpressionDrawer.DrawablesContainer drawables = new ExpressionDrawer.DrawablesContainer();
                if (containerLoader.ForEachElement(debugger, name,
                        delegate (string elName)
                        {
                            ExpressionDrawer.IDrawable drawable = geometryLoader.Load(
                                mreader, debugger, elName, geometryType,callback);
                            if (drawable == null)
                                return false;
                            drawables.Add(drawable);
                            //return callback();
                            return true;
                        }))
                {
                    return drawables;
                }

                return null;
            }

            ContainerLoader containerLoader;
            DrawableLoader geometryLoader;
            string geometryType;
        }

        class PointContainer : PointRange<ExpressionDrawer.MultiPoint>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.MultiPoint; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    // Any type/id, match all containers storing Points

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name, type) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    string pointName, pointType;
                    containerLoader.ElementInfo(name, type, out pointName, out pointType);

                    // WARNING: Potentially recursive call, search for Points only
                    PointLoader pointLoader = loaders.FindByType(ExpressionLoader.Kind.Point,
                                                                 pointName,
                                                                 pointType) as PointLoader;
                    if (pointLoader == null)
                        return null;

                    return new PointContainer(containerLoader, pointLoader, pointType);
                }
            }

            private PointContainer(ContainerLoader containerLoader,
                                   PointLoader pointLoader,
                                   string pointType)
            {
                this.containerLoader = containerLoader;
                this.pointLoader = pointLoader;
                this.pointType = pointType;
            }

            public override Geometry.Traits GetTraits(MemoryReader mreader, Debugger debugger,
                                                      string name)
            {
                return pointLoader.GetTraits(mreader, debugger, name);
            }

            public override ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                            string name, string type,
                                                            LoadCallback callback)
            {
                ExpressionDrawer.MultiPoint result = null;
                
                if (mreader != null)
                {
                    string pointName, dummyType;
                    containerLoader.ElementInfo(name, type, out pointName, out dummyType);

                    result = LoadMemory(mreader, debugger,
                                        name, type,
                                        pointName, pointType, pointLoader,
                                        containerLoader, callback);
                }

                if (result == null)
                {
                    result = LoadParsed(mreader, debugger,
                                        name, type,
                                        pointType, pointLoader, containerLoader,
                                        callback);
                }

                return result;
            }

            ContainerLoader containerLoader;
            PointLoader pointLoader;
            string pointType;
        }

        class ValuesContainer : LoaderR<ExpressionDrawer.ValuesContainer>
        {
            public class LoaderCreator : ExpressionLoader.LoaderCreator
            {
                public bool IsUserDefined() { return false; }
                public Kind Kind() { return ExpressionLoader.Kind.ValuesContainer; }
                public Loader Create(Loaders loaders, Debugger debugger, string name, string type, string id)
                {
                    // Any type/id, match all containers storing unknown element types and assume
                    // they are values

                    ContainerLoader containerLoader = loaders.FindByType(ExpressionLoader.Kind.Container,
                                                                         name,
                                                                         type) as ContainerLoader;
                    if (containerLoader == null)
                        return null;

                    // Element is not a Geometry
                    string elName, elType;
                    containerLoader.ElementInfo(name, type, out elName, out elType);

                    // WARNING: Potentially recursive call, avoid searching for ValuesContainers
                    Loader l = loaders.FindByType(OnlyNonValues, elName, elType);

                    return l == null // this is not non-value
                         ? new ValuesContainer(containerLoader)
                         : null;
                }
            }

            private ValuesContainer(ContainerLoader containerLoader)
            {
                this.containerLoader = containerLoader;
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
                List<double> values = LoadValues(mreader, debugger, name, type, callback);

                return values != null
                     ? new ExpressionDrawer.ValuesContainer(values)
                     : null;
            }

            public List<double> LoadValues(MemoryReader mreader, Debugger debugger,
                                           string name, string type,
                                           LoadCallback callback)
            {
                List<double> result = null;

                if (mreader != null)
                    LoadMemory(mreader, debugger, name, type, containerLoader, out result, callback);

                if (result == null)
                    LoadParsed(debugger, name, containerLoader, out result, callback);

                return result;
            }

            private void LoadMemory(MemoryReader mreader, Debugger debugger,
                                    string name, string type,
                                    ContainerLoader loader,
                                    out List<double> result,
                                    LoadCallback callback)
            {
                result = null;

                string elemName, elemType;
                loader.ElementInfo(name, type, out elemName, out elemType);

                int valSize = ExpressionParser.GetTypeSizeof(debugger, elemType);

                MemoryReader.ValueConverter<double>
                    valueConverter = mreader.GetNumericConverter(elemType, valSize);
                if (valueConverter == null)
                    return;

                List<double> list = new List<double>();
                bool ok = loader.ForEachMemoryBlock(mreader, debugger, name, type, 0, valueConverter,
                    delegate (double[] values)
                    {
                        foreach (double v in values)
                            list.Add(v);
                        return callback();
                    });

                if (ok)
                    result = list;
            }

            private void LoadParsed(Debugger debugger, string name,
                                    ContainerLoader loader,
                                    out List<double> result,
                                    LoadCallback callback)
            {                
                result = null;
                List<double> values = new List<double>();
                bool ok = loader.ForEachElement(debugger, name, delegate (string elName)
                {
                    double value = 0;
                    if (! ExpressionParser.TryLoadDouble(debugger, elName, out value))
                        return false;
                    values.Add(value);
                    return callback();
                });

                if (ok)
                    result = values;
            }

            ContainerLoader containerLoader;
        }

        // This loader is created manually right now so LoaderCreator is not needed
        // TODO: Still, each time Load() is called ValuesContainers Loaders are created
        class CoordinatesContainers : PointRange<ExpressionDrawer.MultiPoint>
        {
            public class LoaderCreator
            {
                public CoordinatesContainers Create(Loaders loaders, Expression[] exprs)
                {
                    int dimension = Math.Min(exprs.Length, 3); // 2 or 3
                    if (dimension < 2)
                        return null;

                    ValuesContainer[] valueContainers = new ValuesContainer[dimension];

                    for (int i = 0; i < dimension; ++i)
                    {
                        valueContainers[i] = loaders.FindByType(ExpressionLoader.Kind.ValuesContainer,
                                                                exprs[i].Name,
                                                                exprs[i].Type) as ValuesContainer;
                        if (valueContainers[i] == null)
                            return null;
                    }

                    Geometry.Traits traits = new Geometry.Traits(dimension, Geometry.CoordinateSystem.Cartesian, Geometry.Unit.None);

                    return new CoordinatesContainers(valueContainers, traits);
                }
            }

            public CoordinatesContainers(ValuesContainer[] valueContainers,
                                         Geometry.Traits traits)
            {
                this.valueContainers = valueContainers;
                this.traits = traits;
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
                return null;
            }

            public Geometry.Traits GetTraits()
            {
                return traits;
            }

            public ExpressionDrawer.IDrawable Load(MemoryReader mreader, Debugger debugger,
                                                   Expression[] exprs,
                                                   LoadCallback callback)
            {
                int dimension = valueContainers.Length;

                List<double>[] coords = new List<double>[dimension];
                for ( int i = 0 ; i < dimension; ++i )
                {
                    coords[i] = valueContainers[i].LoadValues(mreader, debugger,
                                                              exprs[i].Name, exprs[i].Type,
                                                              callback);
                }

                int maxSize = 0;
                foreach(var list in coords)
                {
                    maxSize = Math.Max(maxSize, list.Count);
                }

                ExpressionDrawer.MultiPoint result = new ExpressionDrawer.MultiPoint();

                for (int i = 0; i < maxSize; ++i)
                {
                    double[] coo = new double[dimension];
                    
                    for (int j = 0; j < dimension; ++j)
                    {
                        coo[j] = i < coords[j].Count ? coords[j][i] : 0;
                    }

                    Geometry.Point pt = (dimension >= 3)
                                      ? new Geometry.Point(coo[0], coo[1], coo[2])
                                      : new Geometry.Point(coo[0], coo[1]);
                    result.Add(pt);
                }

                return result;
            }

            ValuesContainer[] valueContainers;
            Geometry.Traits traits;
        }
    }
}
