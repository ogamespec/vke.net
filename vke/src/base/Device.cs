﻿// Copyright (c) 2019  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vulkan;
using static Vulkan.Vk;


namespace vke {
	/// <summary>
	/// Logical device encapsulating vulkan logical device handle. Implements only IDisposable an do not derive from
	/// Activable, so it may be activated only once and no reference counting on it is handled, and no reactivation is posible
	/// after being disposed.
	/// </summary>
	public class Device : IDisposable {
		public readonly PhysicalDevice phy;										/**Vulkan physical device class*/

		VkDevice dev;
		public VkDevice VkDev => dev;                                           /**Vulkan logical device handle*/		


		internal List<Queue> queues = new List<Queue> ();
		internal bool debugMarkersEnabled;

#if MEMORY_POOLS
		public ResourceManager resourceManager;
#endif

		public Device (PhysicalDevice _phy) {
			phy = _phy;
		}

		public void Activate (VkPhysicalDeviceFeatures enabledFeatures, params string[] extensions) {
			List<VkDeviceQueueCreateInfo> qInfos = new List<VkDeviceQueueCreateInfo> ();
			List<List<float>> prioritiesLists = new List<List<float>> ();//store pinned lists for later unpin

			foreach (IGrouping<uint, Queue> qfams in queues.GroupBy (q => q.qFamIndex)) {
				int qTot = qfams.Count ();
				uint qIndex = 0;
				List<float> priorities = new List<float> ();
				bool qCountReached = false;//true when queue count of that family is reached

				foreach (Queue q in qfams) {
					if (!qCountReached)
						priorities.Add (q.priority);
					q.index = qIndex++;
					if (qIndex == phy.QueueFamilies[qfams.Key].queueCount) {
						qIndex = 0;
						qCountReached = true;
					}
				}

				qInfos.Add (new VkDeviceQueueCreateInfo {
					sType = VkStructureType.DeviceQueueCreateInfo,
					queueCount = qCountReached ? phy.QueueFamilies[qfams.Key].queueCount : qIndex,
					queueFamilyIndex = qfams.Key,
					pQueuePriorities = priorities.Pin ()
				});
				prioritiesLists.Add (priorities);//add for unpined
			}

			//enable only supported exceptions
			List<IntPtr> deviceExtensions = new List<IntPtr> ();
			for (int i = 0; i < extensions.Length; i++) {
				if (phy.GetDeviceExtensionSupported (extensions[i])) {
					deviceExtensions.Add (new FixedUtf8String (extensions[i]));
					//store in a bool to prevent frequent string test for debug marker ext presence
					if (extensions[i] == Ext.D.VK_EXT_debug_marker)
						debugMarkersEnabled = true;
				}
			}

			VkDeviceCreateInfo deviceCreateInfo = VkDeviceCreateInfo.New ();
			deviceCreateInfo.queueCreateInfoCount = (uint)qInfos.Count;
			deviceCreateInfo.pQueueCreateInfos = qInfos.Pin ();
			deviceCreateInfo.pEnabledFeatures = enabledFeatures.Pin ();

			if (deviceExtensions.Count > 0) {
				deviceCreateInfo.enabledExtensionCount = (uint)deviceExtensions.Count;
				deviceCreateInfo.ppEnabledExtensionNames = deviceExtensions.Pin ();
			}

			Utils.CheckResult (vkCreateDevice (phy.Handle, ref deviceCreateInfo, IntPtr.Zero, out dev));
			qInfos.Unpin ();
			enabledFeatures.Unpin ();
			foreach (List<float> fa in prioritiesLists)
				fa.Unpin ();

			deviceExtensions.Unpin ();

			//Vk.LoadDeviceFunctionPointers (dev);

			foreach (Queue q in queues)
				q.updateHandle ();

#if MEMORY_POOLS
			resourceManager = new ResourceManager (this);
#endif
		}

		public VkSemaphore CreateSemaphore () {
			VkSemaphore tmp;
			VkSemaphoreCreateInfo info = VkSemaphoreCreateInfo.New ();
			Utils.CheckResult (vkCreateSemaphore (dev, ref info, IntPtr.Zero, out tmp));
			return tmp;
		}
		public void DestroySemaphore (VkSemaphore semaphore) {
			vkDestroySemaphore (dev, semaphore, IntPtr.Zero);
			semaphore = 0;
		}
		public VkFence CreateFence (bool signaled = false) {
			VkFence tmp;
			VkFenceCreateInfo info = VkFenceCreateInfo.New ();
			info.flags = signaled ? VkFenceCreateFlags.Signaled : 0;
			Utils.CheckResult (vkCreateFence (dev, ref info, IntPtr.Zero, out tmp));
			return tmp;
		}
		/// <summary>Destroy the fence.</summary>
		/// <param name="fence">A valid fence handle.</param>
		public void DestroyFence (VkFence fence) {
			vkDestroyFence (dev, fence, IntPtr.Zero);
			fence = 0;
		}
		public void WaitForFence (VkFence fence, ulong timeOut = UInt64.MaxValue) {
			vkWaitForFences (dev, 1, ref fence, 1, timeOut);
		}
		public void ResetFence (VkFence fence) {
			vkResetFences (dev, 1, ref fence);
		}
		public void WaitForFences (VkFence[] fences, ulong timeOut = UInt64.MaxValue) {
			vkWaitForFences (dev, (uint)fences.Length, fences.Pin(), 1, timeOut);
			fences.Unpin ();
		}
		public void ResetFences (params VkFence[] fences) {
			vkResetFences (dev, (uint)fences.Length, fences.Pin());
			fences.Unpin ();
		}

		public void DestroyShaderModule (VkShaderModule module) {
			vkDestroyShaderModule (VkDev, module, IntPtr.Zero);
			module = 0;
		}
		public void WaitIdle () {
			Utils.CheckResult (vkDeviceWaitIdle (dev));
		}
		public VkRenderPass CreateRenderPass (VkRenderPassCreateInfo info) {
			VkRenderPass renderPass;
			Utils.CheckResult (vkCreateRenderPass (dev, ref info, IntPtr.Zero, out renderPass));
			return renderPass;
		}
		internal VkSwapchainKHR CreateSwapChain (VkSwapchainCreateInfoKHR infos) {
			VkSwapchainKHR newSwapChain;
			Utils.CheckResult (vkCreateSwapchainKHR (dev, ref infos, IntPtr.Zero, out newSwapChain));
			return newSwapChain;
		}
		internal void DestroySwapChain (VkSwapchainKHR swapChain) {
			vkDestroySwapchainKHR (dev, swapChain, IntPtr.Zero);
		}
		unsafe public VkImage[] GetSwapChainImages (VkSwapchainKHR swapchain) {
			uint imageCount = 0;
			Utils.CheckResult (vkGetSwapchainImagesKHR (dev, swapchain, out imageCount, IntPtr.Zero));
			if (imageCount == 0)
				throw new Exception ("Swapchain image count is 0.");
			VkImage[] imgs = new VkImage[imageCount];

			Utils.CheckResult (vkGetSwapchainImagesKHR (dev, swapchain, out imageCount, imgs.Pin ()));
			imgs.Unpin ();

			return imgs;
		}
		unsafe public VkImageView CreateImageView (VkImage image, VkFormat format, VkImageViewType viewType = VkImageViewType.ImageView2D, VkImageAspectFlags aspectFlags = VkImageAspectFlags.Color) {
			VkImageView view;
			VkImageViewCreateInfo infos = VkImageViewCreateInfo.New ();
			infos.image = image;
			infos.viewType = viewType;
			infos.format = format;
			infos.components = new VkComponentMapping { r = VkComponentSwizzle.R, g = VkComponentSwizzle.G, b = VkComponentSwizzle.B, a = VkComponentSwizzle.A };
			infos.subresourceRange = new VkImageSubresourceRange (aspectFlags);

			Utils.CheckResult (vkCreateImageView (dev, ref infos, IntPtr.Zero, out view));
			return view;
		}
		public void DestroyImageView (VkImageView view) {
			vkDestroyImageView (dev, view, IntPtr.Zero);
		}
		public void DestroySampler (VkSampler sampler) {
			vkDestroySampler (dev, sampler, IntPtr.Zero);
		}
		public void DestroyImage (VkImage img) {
			vkDestroyImage (dev, img, IntPtr.Zero);
		}
		public void DestroyFramebuffer (VkFramebuffer fb) {
			vkDestroyFramebuffer (dev, fb, IntPtr.Zero);
		}
		public void DestroyRenderPass (VkRenderPass rp) {
			vkDestroyRenderPass (dev, rp, IntPtr.Zero);
		}
		// This function is used to request a Device memory type that supports all the property flags we request (e.g. Device local, host visibile)
		// Upon success it will return the index of the memory type that fits our requestes memory properties
		// This is necessary as implementations can offer an arbitrary number of memory types with different
		// memory properties. 
		// You can check http://vulkan.gpuinfo.org/ for details on different memory configurations
		internal uint GetMemoryTypeIndex (uint typeBits, VkMemoryPropertyFlags properties) {
            // Iterate over all memory types available for the Device used in this example
            for (uint i = 0; i < phy.memoryProperties.memoryTypeCount; i++) {
                if ((typeBits & 1) == 1) {
                    if ((phy.memoryProperties.memoryTypes[i].propertyFlags & properties) == properties) {
                        return i;
                    }
                }
                typeBits >>= 1;
            }

            throw new InvalidOperationException ("Could not find a suitable memory type!");
        }
        public VkFormat GetSuitableDepthFormat () {
            VkFormat[] formats = new VkFormat[] { VkFormat.D32SfloatS8Uint, VkFormat.D32Sfloat, VkFormat.D24UnormS8Uint, VkFormat.D16UnormS8Uint, VkFormat.D16Unorm };
            foreach (VkFormat f in formats) {
                if (phy.GetFormatProperties (f).optimalTilingFeatures.HasFlag(VkFormatFeatureFlags.DepthStencilAttachment))
                    return f;
            }
            throw new InvalidOperationException ("No suitable depth format found.");
        }

        public VkShaderModule LoadSPIRVShader (string filename) {
			VkShaderModule shaderModule;
			using (Stream stream = StaticGetStreamFromPath (filename)) {
				using (BinaryReader br = new BinaryReader (stream)) {
					byte[] shaderCode = br.ReadBytes ((int)stream.Length);
					ulong shaderSize = (ulong)shaderCode.Length;

					// Create a new shader module that will be used for Pipeline creation
					VkShaderModuleCreateInfo moduleCreateInfo = VkShaderModuleCreateInfo.New ();
					moduleCreateInfo.codeSize = new UIntPtr (shaderSize);
					moduleCreateInfo.pCode = shaderCode.Pin ();

					Utils.CheckResult (vkCreateShaderModule (VkDev, ref moduleCreateInfo, IntPtr.Zero, out shaderModule));

					shaderCode.Unpin ();
				}

			}
			return shaderModule;            

        }

		public static Stream StaticGetStreamFromPath (string path) {
			Stream stream = null;

			if (path.StartsWith ("#", StringComparison.Ordinal)) {
				string resId = path.Substring (1);
				//first search entry assembly
				stream = Assembly.GetEntryAssembly ().GetManifestResourceStream (resId);
				if (stream != null)
					return stream;
				//if not found, search assembly named with the 1st element of the resId
				string assemblyName = resId.Split ('.')[0];
				Assembly a = AppDomain.CurrentDomain.GetAssemblies ().FirstOrDefault (aa => aa.GetName ().Name == assemblyName);
				if (a == null)
					throw new Exception ($"Assembly '{assemblyName}' not found for ressource '{path}'.");
				stream = a.GetManifestResourceStream (resId);
				if (stream == null)
					throw new Exception ("Resource not found: " + path);
			} else {
				if (!File.Exists (path))
					throw new FileNotFoundException ("File not found: ", path);
				stream = new FileStream (path, FileMode.Open, FileAccess.Read);
			}
			return stream;
		}

#region IDisposable Support
		private bool disposedValue = false; // Pour détecter les appels redondants

        protected virtual void Dispose (bool disposing) {
            if (!disposedValue) {
				if (disposing) {
#if MEMORY_POOLS
					resourceManager.Dispose ();
#endif
				} else
					System.Diagnostics.Debug.WriteLine ("Device disposed by Finalizer.");

                vkDestroyDevice (dev, IntPtr.Zero);

                disposedValue = true;
            }
        }

        ~Device() {
           Dispose(false);
        }

        // Ce code est ajouté pour implémenter correctement le modèle supprimable.
        public void Dispose () {
            Dispose (true);
            GC.SuppressFinalize(this);
        }
#endregion
    }
}
