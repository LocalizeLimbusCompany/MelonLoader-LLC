﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using HarmonyLib;
using MelonLoader.InternalUtils;
using MelonLoader.Lemons.Cryptography;
using MelonLoader.TinyJSON;
using MelonLoader.Utils;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable 0618

namespace MelonLoader
{
    public static class MelonUtils
    {
        private static readonly Random RandomNumGen = new();
        private static readonly MethodInfo StackFrameGetMethod = typeof(StackFrame).GetMethod("GetMethod", BindingFlags.Instance | BindingFlags.Public);

        internal static void Setup(AppDomain domain)
        {
            using (var sha = SHA256.Create())
                HashCode = string.Join("", sha.ComputeHash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)).Select(b => b.ToString("X")).ToArray());


            Core.WelcomeMessage();

            if (MelonEnvironment.IsMonoRuntime)
                SetCurrentDomainBaseDirectory(MelonEnvironment.GameRootDirectory, domain);

            if (!Directory.Exists(MelonEnvironment.UserDataDirectory))
                Directory.CreateDirectory(MelonEnvironment.UserDataDirectory);

            if (!Directory.Exists(MelonEnvironment.UserLibsDirectory))
                Directory.CreateDirectory(MelonEnvironment.UserLibsDirectory);

            MelonHandler.Setup();
            UnityInformationHandler.Setup();

            CurrentGameAttribute = new MelonGameAttribute(UnityInformationHandler.GameDeveloper, UnityInformationHandler.GameName);
            CurrentPlatform = IsGame32Bit() ? MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X86 : MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X64; // Temporarily
            CurrentDomain = IsGameIl2Cpp() ? MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP : MelonPlatformDomainAttribute.CompatibleDomains.MONO;
        }

        [Obsolete("Use MelonEnvironment.MelonBaseDirectory instead")]
        public static string BaseDirectory => MelonEnvironment.MelonBaseDirectory;
        [Obsolete("Use MelonEnvironment.GameRootDirectory instead")]
        public static string GameDirectory => MelonEnvironment.GameRootDirectory;
        [Obsolete("Use MelonEnvironment.MelonLoaderDirectory instead")]
        public static string MelonLoaderDirectory => MelonEnvironment.MelonLoaderDirectory;
        [Obsolete("Use MelonEnvironment.UserDataDirectory instead")]
        public static string UserDataDirectory => MelonEnvironment.UserDataDirectory;
        [Obsolete("Use MelonEnvironment.UserLibsDirectory instead")]
        public static string UserLibsDirectory => MelonEnvironment.UserLibsDirectory;
        public static MelonPlatformAttribute.CompatiblePlatforms CurrentPlatform { get; private set; }
        public static MelonPlatformDomainAttribute.CompatibleDomains CurrentDomain { get; private set; }
        public static MelonGameAttribute CurrentGameAttribute { get; private set; }
        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T> { if (value.CompareTo(min) < 0) return min; if (value.CompareTo(max) > 0) return max; return value; }
        public static string HashCode { get; private set; }

        public static int RandomInt()
        {
            lock (RandomNumGen)
                return RandomNumGen.Next();
        }

        public static int RandomInt(int max)
        {
            lock (RandomNumGen)
                return RandomNumGen.Next(max);
        }

        public static int RandomInt(int min, int max)
        {
            lock (RandomNumGen)
                return RandomNumGen.Next(min, max);
        }

        public static double RandomDouble()
        {
            lock (RandomNumGen)
                return RandomNumGen.NextDouble();
        }

        public static string RandomString(int length)
        {
            StringBuilder builder = new();
            for (int i = 0; i < length; i++)
                builder.Append(Convert.ToChar(Convert.ToInt32(Math.Floor(25 * RandomDouble())) + 65));
            return builder.ToString();
        }

        public static PlatformID GetPlatform => Environment.OSVersion.Platform;

        public static bool IsUnix => GetPlatform is PlatformID.Unix;
        public static bool IsWindows => GetPlatform is PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE;
        public static bool IsMac => GetPlatform is PlatformID.MacOSX;

        public static void SetCurrentDomainBaseDirectory(string dirpath, AppDomain domain = null)
        {
            if (MelonEnvironment.IsDotnetRuntime)
                return;

            if (domain == null)
                domain = AppDomain.CurrentDomain;
            try
            {
                ((AppDomainSetup)typeof(AppDomain).GetProperty("SetupInformationNoCopy", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(domain, new object[0]))
                    .SetApplicationBase(dirpath);
            }
            catch (Exception ex) { MelonLogger.Warning($"AppDomainSetup.ApplicationBase Exception: {ex}"); }
            Directory.SetCurrentDirectory(dirpath);
        }

        public static MelonBase GetMelonFromStackTrace()
        {
            StackTrace st = new(3, true);
            return GetMelonFromStackTrace(st);
        }

        public static MelonBase GetMelonFromStackTrace(StackTrace st, bool allFrames = false)
        {
            if (st.FrameCount <= 0)
                return null;

            if (allFrames)
            {
                foreach (StackFrame frame in st.GetFrames())
                {
                    MelonBase ret = CheckForMelonInFrame(frame);
                    if (ret != null)
                        return ret;
                }
                return null;

            }

            MelonBase output = CheckForMelonInFrame(st);
            if (output == null)
                output = CheckForMelonInFrame(st, 1);
            if (output == null)
                output = CheckForMelonInFrame(st, 2);
            return output;
        }

        private static MelonBase CheckForMelonInFrame(StackTrace st, int frame = 0)
        {
            StackFrame sf = st.GetFrame(frame);
            if (sf == null)
                return null;

            return CheckForMelonInFrame(sf);
        }

        private static MelonBase CheckForMelonInFrame(StackFrame sf)
            //The JIT compiler on .NET 6 on Windows 10 (win11 is fine, somehow) really doesn't like us calling StackFrame.GetMethod here
            //Rather than trying to work out why, I'm just going to call it via reflection.
            => GetMelonFromAssembly(((MethodBase)StackFrameGetMethod.Invoke(sf, new object[0]))?.DeclaringType?.Assembly);

        private static MelonBase GetMelonFromAssembly(Assembly asm)
            => asm == null ? null : MelonHandler.Plugins.Cast<MelonBase>().FirstOrDefault(x => x.Assembly == asm) ?? MelonHandler.Mods.FirstOrDefault(x => x.Assembly == asm);

        public static string ComputeSimpleSHA256Hash(string filePath)
        {
            if (!File.Exists(filePath))
                return "null";

            byte[] byteHash = LemonSHA256.ComputeSHA256Hash(File.ReadAllBytes(filePath));
            string finalHash = string.Empty;
            foreach (byte b in byteHash)
                finalHash += b.ToString("x2");

            return finalHash;
        }

        public static T ParseJSONStringtoStruct<T>(string jsonstr)
        {
            if (string.IsNullOrEmpty(jsonstr))
                return default;
            Variant jsonarr;
            try { jsonarr = JSON.Load(jsonstr); }
            catch (Exception ex)
            {
                MelonLogger.Error($"Exception while Decoding JSON String to JSON Variant: {ex}");
                return default;
            }
            if (jsonarr == null)
                return default;
            T returnobj = default;
            try { returnobj = jsonarr.Make<T>(); }
            catch (Exception ex) { MelonLogger.Error($"Exception while Converting JSON Variant to {typeof(T).Name}: {ex}"); }
            return returnobj;
        }

        public static T PullAttributeFromAssembly<T>(Assembly asm, bool inherit = false) where T : Attribute
        {
            T[] attributetbl = PullAttributesFromAssembly<T>(asm, inherit);
            if ((attributetbl == null) || (attributetbl.Length <= 0))
                return null;
            return attributetbl[0];
        }

        public static T[] PullAttributesFromAssembly<T>(Assembly asm, bool inherit = false) where T : Attribute
        {
            Attribute[] att_tbl = Attribute.GetCustomAttributes(asm, inherit);

            if ((att_tbl == null) || (att_tbl.Length <= 0))
                return null;

            Type requestedType = typeof(T);
            string requestedAssemblyName = requestedType.Assembly.GetName().Name;
            List<T> output = new();
            foreach (Attribute att in att_tbl)
            {
                Type attType = att.GetType();
                string attAssemblyName = attType.Assembly.GetName().Name;

                if ((attType == requestedType)
                    || IsTypeEqualToFullName(attType, requestedType.FullName)
                    || ((attAssemblyName.Equals("MelonLoader")
                        || attAssemblyName.Equals("MelonLoader.ModHandler"))
                        && (requestedAssemblyName.Equals("MelonLoader")
                        || requestedAssemblyName.Equals("MelonLoader.ModHandler"))
                        && IsTypeEqualToName(attType, requestedType.Name)))
                    output.Add(att as T);
            }

            return output.ToArray();
        }

        public static bool IsTypeEqualToName(Type type1, string type2)
            => type1.Name == type2 || (type1 != typeof(object) && IsTypeEqualToName(type1.BaseType, type2));

        public static bool IsTypeEqualToFullName(Type type1, string type2)
            => type1.FullName == type2 || (type1 != typeof(object) && IsTypeEqualToFullName(type1.BaseType, type2));

        public static string MakePlural(this string str, int amount)
            => amount == 1 ? str : $"{str}s";

        public static IEnumerable<Type> GetValidTypes(this Assembly asm)
            => GetValidTypes(asm, null);

        public static IEnumerable<Type> GetValidTypes(this Assembly asm, LemonFunc<Type, bool> predicate)
        {
            IEnumerable<Type> returnval = Enumerable.Empty<Type>();
            try { returnval = asm.GetTypes().AsEnumerable(); }
            catch (ReflectionTypeLoadException ex)
            {
                MelonLogger.Error($"Failed to load all types in assembly {asm.FullName} due to: {ex.Message}", ex);
                //Console.WriteLine(ex);
                returnval = ex.Types;
            }

            return returnval.Where(x => (x != null) && (predicate == null || predicate(x)));
        }

        public static bool IsNotImplemented(this MethodBase methodBase)
        {
            if (methodBase == null)
                throw new ArgumentNullException(nameof(methodBase));

            DynamicMethodDefinition method = methodBase.ToNewDynamicMethodDefinition();
            ILContext ilcontext = new(method.Definition);
            ILCursor ilcursor = new(ilcontext);

            bool returnval = (ilcursor.Instrs.Count == 2)
                && (ilcursor.Instrs[1].OpCode.Code == Mono.Cecil.Cil.Code.Throw);

            ilcontext.Dispose();
            method.Dispose();
            return returnval;
        }

        public static HarmonyMethod ToNewHarmonyMethod(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
                throw new ArgumentNullException(nameof(methodInfo));
            return new HarmonyMethod(methodInfo);
        }


        public static DynamicMethodDefinition ToNewDynamicMethodDefinition(this MethodBase methodBase)
        {
            if (methodBase == null)
                throw new ArgumentNullException(nameof(methodBase));
            return new DynamicMethodDefinition(methodBase);
        }

        private static FieldInfo AppDomainSetup_application_base;
        public static void SetApplicationBase(this AppDomainSetup _this, string value)
        {
            if (AppDomainSetup_application_base == null)
                AppDomainSetup_application_base = typeof(AppDomainSetup).GetField("application_base", BindingFlags.NonPublic | BindingFlags.Instance);
            AppDomainSetup_application_base?.SetValue(_this, value);
        }

        private static FieldInfo HashAlgorithm_HashSizeValue;
        public static void SetHashSizeValue(this HashAlgorithm _this, int value)
        {
            if (HashAlgorithm_HashSizeValue == null)
                HashAlgorithm_HashSizeValue = typeof(HashAlgorithm).GetField("HashSizeValue", BindingFlags.Public | BindingFlags.Instance);
            HashAlgorithm_HashSizeValue?.SetValue(_this, value);
        }

        // Modified Version of System.IO.Path.HasExtension from .NET Framework's mscorlib.dll
        public static bool ContainsExtension(this string path)
        {
            if (path != null)
            {
                path.CheckInvalidPathChars();
                int num = path.Length;
                while (--num >= 0)
                {
                    char c = path[num];
                    if (c == '.')
                        return num != path.Length - 1;
                    if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || c == Path.VolumeSeparatorChar)
                        break;
                }
            }
            return false;
        }

        // Modified Version of System.IO.Path.CheckInvalidPathChars from .NET Framework's mscorlib.dll
        private static void CheckInvalidPathChars(this string path)
        {
            foreach (int num in path)
                if (num == 34 || num == 60 || num == 62 || num == 124 || num < 32)
                    throw new ArgumentException("Argument_InvalidPathChars", nameof(path));
        }

        public static void GetDelegate<T>(this IntPtr ptr, out T output) where T : Delegate
            => output = GetDelegate<T>(ptr);
        public static T GetDelegate<T>(this IntPtr ptr) where T : Delegate
            => GetDelegate(ptr, typeof(T)) as T;
        public static Delegate GetDelegate(this IntPtr ptr, Type type)
        {
            if (ptr == IntPtr.Zero)
                throw new ArgumentNullException(nameof(ptr));
            Delegate del = Marshal.GetDelegateForFunctionPointer(ptr, type);
            if (del == null)
                throw new Exception($"Unable to Get Delegate of Type {type.FullName} for Function Pointer!");
            return del;
        }
        public static IntPtr GetFunctionPointer(this Delegate del)
            => Marshal.GetFunctionPointerForDelegate(del);

        public static NativeLibrary ToNewNativeLibrary(this IntPtr ptr)
            => new(ptr);
        public static NativeLibrary<T> ToNewNativeLibrary<T>(this IntPtr ptr)
            => new(ptr);
        public static IntPtr GetNativeLibraryExport(this IntPtr ptr, string name)
            => NativeLibrary.GetExport(ptr, name);

        public static ClassPackageFile LoadIncludedClassPackage(this AssetsManager assetsManager)
        {
            ClassPackageFile classPackage = null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MelonLoader.Resources.classdata.tpk"))
                classPackage = assetsManager.LoadClassPackage(stream);
            return classPackage;
        }

        [Obsolete("MelonLoader.MelonUtils.GetUnityVersion() is obsolete. Please use MelonLoader.InternalUtils.UnityInformationHandler.EngineVersion instead.")]
        public static string GetUnityVersion() => UnityInformationHandler.EngineVersion.ToStringWithoutType();
        [Obsolete("MelonLoader.MelonUtils.GameDeveloper is obsolete. Please use MelonLoader.InternalUtils.UnityInformationHandler.GameDeveloper instead.")]
        public static string GameDeveloper { get => UnityInformationHandler.GameDeveloper; }
        [Obsolete("MelonLoader.MelonUtils.GameName is obsolete. Please use MelonLoader.InternalUtils.UnityInformationHandler.GameName instead.")]
        public static string GameName { get => UnityInformationHandler.GameName; }
        [Obsolete("MelonLoader.MelonUtils.GameVersion is obsolete. Please use MelonLoader.InternalUtils.UnityInformationHandler.GameVersion instead.")]
        public static string GameVersion { get => UnityInformationHandler.GameVersion; }


#if !NET6_0
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern static bool IsGame32Bit();
#else
        public static bool IsGame32Bit() => !Environment.Is64BitProcess;
#endif


        public static bool IsGameIl2Cpp() => Directory.Exists(MelonEnvironment.Il2CppDataDirectory);

        public static bool IsOldMono() => File.Exists(MelonEnvironment.UnityGameDataDirectory + "\\Mono\\mono.dll") ||
                                          File.Exists(MelonEnvironment.UnityGameDataDirectory + "\\Mono\\libmono.so");

        public static bool IsUnderWineOrSteamProton() => Core.WineGetVersion is not null;

        [Obsolete("Use MelonEnvironment.GameExecutablePath instead")]
        public static string GetApplicationPath() => MelonEnvironment.GameExecutablePath;

        [Obsolete("Use MelonEnvironment.UnityGameDataDirectory instead")]
        public static string GetGameDataDirectory() => MelonEnvironment.UnityGameDataDirectory;

        [Obsolete("Use MelonEnvironment.MelonManagedDirectory instead")]
        public static string GetManagedDirectory() => MelonEnvironment.MelonManagedDirectory;

        public static void SetConsoleTitle(string title)
        {
            if (!MelonLaunchOptions.Console.ShouldSetTitle || MelonLaunchOptions.Console.ShouldHide)
                return;

            Console.Title = title;
        }

        public static string GetFileProductName(string filepath)
        {
            var fileInfo = FileVersionInfo.GetVersionInfo(filepath);
            if (fileInfo != null)
                return fileInfo.ProductName;
            return null;
        }

        [Obsolete("Use NativeUtils.NativeHook instead")]
        public static void NativeHookAttach(IntPtr target, IntPtr detour) => BootstrapInterop.NativeHookAttach(target, detour);

#if NET6_0
        internal static void NativeHookAttachDirect(IntPtr target, IntPtr detour) => BootstrapInterop.NativeHookAttachDirect(target, detour);
#else
        //On mono, NativeHookAttach *is* direct.
        [Obsolete("Use NativeUtils.NativeHook instead")]
        internal static void NativeHookAttachDirect(IntPtr target, IntPtr detour) => BootstrapInterop.NativeHookAttach(target, detour);
#endif
        [Obsolete("Use NativeUtils.NativeHook instead")]
        public static void NativeHookDetach(IntPtr target, IntPtr detour) => BootstrapInterop.NativeHookDetach(target, detour);


        //Removing these as they're private so mods shouldn't need them
        //Can potentially be redirected to MelonEnvironment if really needed.

        //[MethodImpl(MethodImplOptions.InternalCall)]
        //[return: MarshalAs(UnmanagedType.LPStr)]
        //private extern static string Internal_GetBaseDirectory();
        //[MethodImpl(MethodImplOptions.InternalCall)]
        //[return: MarshalAs(UnmanagedType.LPStr)]
        //private extern static string Internal_GetGameDirectory();
    }
}