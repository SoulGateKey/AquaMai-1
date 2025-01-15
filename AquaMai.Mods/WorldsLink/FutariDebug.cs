using System.Collections.Generic;
using AquaMai.Config.Attributes;
using DB;
using HarmonyLib;
using Manager.Party.Party;
using PartyLink;

namespace AquaMai.Mods.WorldsLink;

[ConfigSection]
public class FutariDebug
{
    // Log ListenSocket creation
    // ListenSocket:: public ListenSocket(string name, int mockID)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ListenSocket), MethodType.Constructor, typeof(string), typeof(int))]
    private static void ListenSocket(ListenSocket __instance, string name, int mockID)
    {
        Log.Debug($"new ListenSocket({name}, {mockID})");
    }
    
    // Log ListenSocket open
    // ListenSocket:: public bool open(ushort portNumber)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ListenSocket), "open", typeof(ushort))]
    private static bool open(ListenSocket __instance, ushort portNumber)
    {
        Log.Debug($"ListenSocket.open({portNumber}) - {__instance}");
        return PrefixRet.RUN_ORIGINAL;
    }

    // Log packet type
    // Analyzer:: private void procPacketData(Packet packet)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Analyzer), "procPacketData", typeof(Packet))]
    private static bool procPacketData(Packet packet, Dictionary<Command, object> ____commandMap)
    {
        var keys = string.Join(", ", ____commandMap.Keys);
        Log.Debug($"procPacketData: {Log.BRIGHT_RED}{packet.getCommand()}{Log.RESET} in {keys}");
        return PrefixRet.RUN_ORIGINAL;
    }

    // Log host creation
    // Host:: public Host(string name)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Host), MethodType.Constructor, typeof(string))]
    private static void Host(Host __instance, string name)
    {
        Log.Debug($"new Host({name})");
    }
    
    // Log host state change
    // Host:: private void SetCurrentStateID(PartyPartyHostStateID nextState)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Host), "SetCurrentStateID", typeof(PartyPartyHostStateID))]
    private static bool SetCurrentStateID(PartyPartyHostStateID nextState)
    {
        Log.Debug($"Host::SetCurrentStateID: {nextState}");
        return PrefixRet.RUN_ORIGINAL;
    }
    
    // Log Member creation
    // Member:: public Member(string name, Host host, NFSocket socket)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Member), MethodType.Constructor, typeof(string), typeof(Host), typeof(NFSocket))]
    private static void Member(Member __instance, string name, Host host, NFSocket socket)
    {
        Log.Debug($"new Member({name}, {host}, {socket})");
    }
    
    // Log Member state change
    // Member:: public void SetCurrentStateID(PartyPartyClientStateID state)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Member), "SetCurrentStateID", typeof(PartyPartyClientStateID))]
    private static bool SetCurrentStateID(PartyPartyClientStateID state)
    {
        Log.Debug($"Member::SetCurrentStateID: {state}");
        return PrefixRet.RUN_ORIGINAL;
    }
    
    // Log Member RecvRequestJoin
    // Member:: private void RecvRequestJoin(Packet packet)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Member), "RecvRequestJoin", typeof(Packet))]
    private static bool RecvRequestJoin(Packet packet)
    {
        Log.Debug($"Member::RecvRequestJoin: {packet.getParam<RequestJoin>()}");
        return PrefixRet.RUN_ORIGINAL;
    }
    
    // Log Member RecvClientState
    // Member:: private void RecvClientState(Packet packet)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Member), "RecvClientState", typeof(Packet))]
    private static bool RecvClientState(Packet packet)
    {
        Log.Debug($"Member::RecvClientState: {packet.getParam<ClientState>()}");
        return PrefixRet.RUN_ORIGINAL;
    }
}