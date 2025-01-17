using System;
using System.Text;
using Net;
using Net.Packet;
using MelonLoader;
using MelonLoader.TinyJSON;
using HarmonyLib;
using AquaMai.Core.Helpers;
using System.Linq;

namespace AquaMai.Mods.Utils;

public class NetPacketExtension
{
    public delegate void NetPacketResponseHandler(string api, Variant json);

    public static event NetPacketResponseHandler OnNetPacketResponse;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Packet), "ProcImpl")]
    public static void PreProcImpl(Packet __instance)
    {
        try
        {
            if (
                __instance.State == PacketState.Process &&
                Traverse.Create(__instance).Field("Client").GetValue() is NetHttpClient client &&
                client.State == NetHttpClient.StateDone)
            {
                var netQuery = __instance.Query;
                var api = Shim.RemoveApiSuffix(netQuery.Api);
                var response = client.GetResponse().ToArray();
                var decryptedResponse = Shim.NetHttpClientDecryptsResponse ? response : Shim.DecryptNetPacketBody(response);
                var json = JSON.Load(Encoding.UTF8.GetString(decryptedResponse));
                foreach (var handler in OnNetPacketResponse?.GetInvocationList())
                {
                    try
                    {
                        handler.DynamicInvoke(api, json);
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Error($"[NetPacketExtension] Error in handler: {e}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[NetPacketExtension] Failed to process NetPacket: {e}");
        }
    }
}
