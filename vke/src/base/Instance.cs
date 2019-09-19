﻿//
// Instance.cs
//
// Author:
//       Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// Copyright (c) 2019 jp
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vulkan;
using static Vulkan.Vk;

namespace vke {
	/// <summary>
	/// Vulkan Instance disposable class
	/// </summary>
	public class Instance : IDisposable {
		/// <summary>If true, the VK_LAYER_KHRONOS_validation layer is loaded at startup; </summary>
		public static bool VALIDATION;
		/// <summary>If true, the VK_LAYER_RENDERDOC_Capture layer is loaded at startup; </summary>
		public static bool RENDER_DOC_CAPTURE;

		public static uint VK_MAJOR = 1;
		public static uint VK_MINOR = 1;

		public static string ENGINE_NAME = "vke.net";
		public static string APPLICATION_NAME = "vke.net";

		VkInstance inst;

		public IntPtr Handle => inst.Handle;
		public VkInstance VkInstance => inst;


		static class Strings {

			public static FixedUtf8String main = "main";
		}
		const string strValidationLayer = "VK_LAYER_KHRONOS_validation";
		const string strRenderDocLayer = "VK_LAYER_RENDERDOC_Capture";

		/// <summary>
		/// Create a new vulkan instance with enabled extensions given as argument.
		/// </summary>
		/// <param name="extensions">List of extension to enable if supported</param>
		public Instance (params string[] extensions) {
			List<IntPtr> instanceExtensions = new List<IntPtr> ();
			List<IntPtr> enabledLayerNames = new List<IntPtr> ();

			string[] supportedExts = SupportedExtensions (IntPtr.Zero);

			using (PinnedObjects pctx = new PinnedObjects ()) {
				for (int i = 0; i < extensions.Length; i++) {
					if (supportedExts.Contains (extensions[i]))
						instanceExtensions.Add (extensions[i].Pin (pctx));
					else
						Console.WriteLine ($"Vulkan initialisation: Unsupported extension: {extensions[i]}");
				}


				if (VALIDATION) 
					enabledLayerNames.Add (strValidationLayer.Pin (pctx));
				if (RENDER_DOC_CAPTURE)
					enabledLayerNames.Add (strRenderDocLayer.Pin (pctx));


				VkApplicationInfo appInfo = new VkApplicationInfo () {
					sType = VkStructureType.ApplicationInfo,
					apiVersion = new Vulkan.Version (VK_MAJOR, VK_MINOR, 0),
					pApplicationName = ENGINE_NAME.Pin (pctx),
					pEngineName = APPLICATION_NAME.Pin (pctx),
				};

				VkInstanceCreateInfo instanceCreateInfo = VkInstanceCreateInfo.New ();
				instanceCreateInfo.pApplicationInfo = appInfo.Pin (pctx);

				if (instanceExtensions.Count > 0) {
					instanceCreateInfo.enabledExtensionCount = (uint)instanceExtensions.Count;
					instanceCreateInfo.ppEnabledExtensionNames = instanceExtensions.Pin (pctx);
				}
				if (enabledLayerNames.Count > 0) {
					instanceCreateInfo.enabledLayerCount = (uint)enabledLayerNames.Count;
					instanceCreateInfo.ppEnabledLayerNames = enabledLayerNames.Pin (pctx);
				}

				VkResult result = vkCreateInstance (ref instanceCreateInfo, IntPtr.Zero, out inst);
				if (result != VkResult.Success)
					throw new InvalidOperationException ("Could not create Vulkan instance. Error: " + result);

				Vk.LoadInstanceFunctionPointers (inst);
			}
		}

		public string[] SupportedExtensions (IntPtr layer) {
			Utils.CheckResult (vkEnumerateInstanceExtensionProperties (layer, out uint count, IntPtr.Zero));

			int sizeStruct = Marshal.SizeOf<VkExtensionProperties> ();
			IntPtr ptrSupExts = Marshal.AllocHGlobal (sizeStruct * (int)count);
			Utils.CheckResult (vkEnumerateInstanceExtensionProperties (layer, out count, ptrSupExts));

			string[] result = new string[count];
			IntPtr tmp = ptrSupExts;
			for (int i = 0; i < count; i++) {
				result[i] = Marshal.PtrToStringAnsi (tmp);
				tmp += sizeStruct;
			}

			Marshal.FreeHGlobal (ptrSupExts);
			return result;
		}

		public PhysicalDeviceCollection GetAvailablePhysicalDevice () => new PhysicalDeviceCollection (inst);
		/// <summary>
		/// Create a new vulkan surface from native window pointer
		/// </summary>
		public VkSurfaceKHR CreateSurface (IntPtr hWindow) {
			ulong surf;
			Utils.CheckResult ((VkResult)Glfw.Glfw3.CreateWindowSurface (inst.Handle, hWindow, IntPtr.Zero, out surf), "Create Surface Failed.");
			return surf;
		}
		public void GetDelegate<T> (string name, out T del) {
			using (FixedUtf8String n = new FixedUtf8String (name)) {
				del = Marshal.GetDelegateForFunctionPointer<T> (vkGetInstanceProcAddr (Handle, (IntPtr)n));
			}
		}

		#region IDisposable Support
		private bool disposedValue = false;

		protected virtual void Dispose (bool disposing) {
			if (!disposedValue) {
				if (disposing) {
					// TODO: supprimer l'état managé (objets managés).
				} else
					System.Diagnostics.Debug.WriteLine ("Instance disposed by Finalizer");

				vkDestroyInstance (inst, IntPtr.Zero);

				disposedValue = true;
			}
		}

		~Instance () {
			Dispose (false);
		}

		public void Dispose () {
			Dispose (true);
			GC.SuppressFinalize (this);
		}
		#endregion
	}
}
