using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AquaMai.Config.Attributes;
using DB;
using HarmonyLib;
using Manager;
using PartyLink;
using Process;
using Manager.Party.Party;
using AquaMai.Core.Attributes;

namespace AquaMai.Mods.WorldsLink;

[ConfigSection(
    en: "Enable WorldsLink Multiplayer",
    zh: "启用 WorldsLink 多人游戏",
    defaultOn: true)]
public static class Futari
{
    private static readonly Dictionary<NFSocket, FutariSocket> redirect = new();
    private static FutariClient client;
    private static bool isInit = false;

    private static MethodBase packetWriteUInt;
    private static System.Type StartUpStateType;

    [ConfigEntry(hideWhenDefault: true)]
    public static bool Debug = false;

    #region Init
    
    public static void OnBeforePatch()
    {
        Log.Info("Starting WorldsLink patch...");

        packetWriteUInt = typeof(Packet).GetMethod("write_uint", BindingFlags.NonPublic | BindingFlags.Static, null,
            [typeof(PacketType), typeof(int), typeof(uint)], null);
        if (packetWriteUInt == null) Log.Error("write_uint not found");

        StartUpStateType = typeof(StartupProcess).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType;
        if (StartUpStateType == null) Log.Error("StartUpStateType not found");
    
        // TODO: Make IP configurable
        client = new FutariClient("A1234567890", "futari.aquadx.net", 20101);
    }

    // Entrypoint
    // private void CheckAuth_Proc()
    [HarmonyPrefix]
    [HarmonyPatch(typeof(OperationManager), "CheckAuth_Proc")]
    private static bool CheckAuth_Proc()
    {
        if (isInit) return PrefixRet.RUN_ORIGINAL;
        Log.Info("CheckAuth_Proc");

        var keychip = AMDaemon.System.KeychipId.ShortValue;
        Log.Info($"Keychip ID: {keychip}");
        if (string.IsNullOrEmpty(keychip)) Log.Error("Keychip ID is empty. WorldsLink will not work.");
        client.keychip = keychip;
        client.ConnectAsync();

        isInit = true;
        return PrefixRet.RUN_ORIGINAL;
    }
    
    #endregion
    
    #region Misc

    // Block irrelevant packets
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SocketBase), "sendClass", typeof(ICommandParam))]
    private static bool sendClass(SocketBase __instance, ICommandParam info)
    {
        // Block AdvocateDelivery, SettingHostAddress
        if (info is AdvocateDelivery or Setting.SettingHostAddress) return PrefixRet.BLOCK_ORIGINAL;
        
        // For logging only, log the actual type of info and the actual type of this class
        Log.Debug($"SendClass: {Log.BRIGHT_RED}{info.GetType().Name}{Log.RESET} from {__instance.GetType().Name}");
        return PrefixRet.RUN_ORIGINAL;
    }

    // Patch for error logging
    // SocketBase:: protected void error(string message, int no)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SocketBase), "error", typeof(string), typeof(int))]
    private static bool error(string message, int no)
    {
        Log.Error($"Error: {message} ({no})");
        return PrefixRet.RUN_ORIGINAL;
    }

    // Force isSameVersion to return true
    // Packet:: public bool isSameVersion()
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Packet), "isSameVersion")]
    private static void isSameVersion(ref bool __result)
    {
        Log.Debug($"isSameVersion (original): {__result}, forcing true");
        __result = true;
    }
    
    // Patch my IP address to a stub
    // public static IPAddress MyIpAddress(int mockID)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PartyLink.Util), "MyIpAddress", typeof(int))]
    private static bool MyIpAddress(int mockID, ref IPAddress __result)
    {
        __result = FutariExt.MyStubIP().ToIP();
        return PrefixRet.BLOCK_ORIGINAL;
    }

    #endregion

    //Skip StartupNetworkChecker
    [HarmonyPostfix]
    [HarmonyPatch("StartupProcess", nameof(StartupProcess.OnUpdate))]
    private static void SkipStartupNetworkCheck(ref byte ____state, string[] ____statusMsg , string[] ____statusSubMsg)
    {
        //Title
        ____statusMsg[7] = "WORLD LINK";
        switch (client.StatusCode)
        {
            case -1:
                ____statusSubMsg[7] = "BAD";
                break;
            case 0:
                ____statusSubMsg[7] = "Not Connect";
                break;
            case 1:
                ____statusSubMsg[7] = "Connecting";
                break;
            case 2:
                ____statusSubMsg[7] = "GOOD";
                break;

            default:
                ____statusSubMsg[7] = "Waiting...";
                break;
        }
        //Ping
        ____statusMsg[8] = "PING";
        ____statusSubMsg[8]= client._delayAvg==0?"N/A":client._delayAvg.ToString()+"ms";
        //
        ____statusMsg[9] = "";
        ____statusSubMsg[9] = "";
        //Skip Oragin Init And Manual Init Party
        if (____state == 0x04/*StartupProcess.StartUpState.WaitLinkDelivery*/)
        {
            ____state = 0x08;//StartupProcess.StartUpState.Ready
            DeliveryChecker.get().start(true);
            Setting.Data data = new Setting.Data();
            data.set(false,4);
            Setting.get().setData(data);
            Setting.get().setRetryEnable(true);
            Advertise.get().initialize(MachineGroupID.ON);
            Manager.Party.Party.Party.Get().Start(MachineGroupID.ON);
            Log.Info("Skip Startup Network Check");
        }
    }

    #region NFSocket
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NFSocket), MethodType.Constructor, typeof(AddressFamily), typeof(SocketType), typeof(ProtocolType), typeof(int))]
    private static void NFCreate(NFSocket __instance, AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, int mockID)
    {
        Log.Debug($"new NFSocket({addressFamily}, {socketType}, {protocolType}, {mockID})");
        if (mockID == 3939) return;  // Created in redirected NFAccept as a stub
        var futari = new FutariSocket(addressFamily, socketType, protocolType, mockID);
        redirect.Add(__instance, futari);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NFSocket), MethodType.Constructor, typeof(Socket))]
    private static void NFCreate2(NFSocket __instance, Socket nfSocket)
    {
        Log.Error("new NFSocket(Socket) -- We shouldn't get here.");
        throw new NotImplementedException();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Poll")]
    private static bool NFPoll(NFSocket socket, SelectMode mode, ref bool __result)
    {
        __result = FutariSocket.Poll(redirect[socket], mode);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Send")]
    private static bool NFSend(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, ref int __result)
    {
        __result = redirect[__instance].Send(buffer, offset, size, socketFlags);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "SendTo")]
    private static bool NFSendTo(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEP, ref int __result)
    {
        __result = redirect[__instance].SendTo(buffer, offset, size, socketFlags, remoteEP);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Receive")]
    private static bool NFReceive(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, out SocketError errorCode, ref int __result)
    {
        __result = redirect[__instance].Receive(buffer, offset, size, socketFlags, out errorCode);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "ReceiveFrom")]
    private static bool NFReceiveFrom(NFSocket __instance, byte[] buffer, SocketFlags socketFlags, ref EndPoint remoteEP, ref int __result)
    {
        __result = redirect[__instance].ReceiveFrom(buffer, socketFlags, ref remoteEP);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Bind")]
    private static bool NFBind(NFSocket __instance, EndPoint localEndP)
    {
        Log.Debug("NFBind");
        redirect[__instance].Bind(localEndP);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Listen")]
    private static bool NFListen(NFSocket __instance, int backlog)
    {
        Log.Debug("NFListen");
        redirect[__instance].Listen(backlog);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Accept")]
    private static bool NFAccept(NFSocket __instance, ref NFSocket __result)
    {
        Log.Debug("NFAccept");
        var futariSocket = redirect[__instance].Accept();
        var mockSocket = new NFSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, 3939);
        redirect[mockSocket] = futariSocket;
        __result = mockSocket;
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "ConnectAsync")]
    private static bool NFConnectAsync(NFSocket __instance, SocketAsyncEventArgs e, int mockID, ref bool __result)
    {
        Log.Debug("NFConnectAsync");
        __result = redirect[__instance].ConnectAsync(e, mockID);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "SetSocketOption")]
    private static bool NFSetSocketOption(NFSocket __instance, SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue)
    {
        redirect[__instance].SetSocketOption(optionLevel, optionName, optionValue);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Close")]
    private static bool NFClose(NFSocket __instance)
    {
        Log.Debug("NFClose");
        redirect[__instance].Close();
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Shutdown")]
    private static bool NFShutdown(NFSocket __instance, SocketShutdown how)
    {
        Log.Debug("NFShutdown");
        redirect[__instance].Shutdown(how);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "RemoteEndPoint", MethodType.Getter)]
    private static bool NFGetRemoteEndPoint(NFSocket __instance, ref EndPoint __result)
    {
        Log.Debug("NFGetRemoteEndPoint");
        __result = redirect[__instance].RemoteEndPoint;
        return PrefixRet.BLOCK_ORIGINAL;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "LocalEndPoint", MethodType.Getter)]
    private static bool NFGetLocalEndPoint(NFSocket __instance, ref EndPoint __result)
    {
        Log.Debug("NFGetLocalEndPoint");
        __result = redirect[__instance].LocalEndPoint;
        return PrefixRet.BLOCK_ORIGINAL;
    }
    #endregion

    #region Packet codec

    // Disable encryption
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Packet), "encrypt")]
    private static bool PacketEncrypt(Packet __instance, PacketType ____encrypt, PacketType ____plane)
    {
        ____encrypt.ClearAndResize(____plane.Count);
        Array.Copy(____plane.GetBuffer(), 0, ____encrypt.GetBuffer(), 0, ____plane.Count);
        ____encrypt.ChangeCount(____plane.Count);
        packetWriteUInt.Invoke(null, [____plane, 0, (uint)____plane.Count]);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    // Disable decryption
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Packet), "decrypt")]
    private static bool PacketDecrypt(Packet __instance, PacketType ____encrypt, PacketType ____plane)
    {
        ____plane.ClearAndResize(____encrypt.Count);
        Array.Copy(____encrypt.GetBuffer(), 0, ____plane.GetBuffer(), 0, ____encrypt.Count);
        ____plane.ChangeCount(____encrypt.Count);
        packetWriteUInt.Invoke(null, [____plane, 0, (uint)____plane.Count]);
        return PrefixRet.BLOCK_ORIGINAL;
    }

    #endregion

    #region Debug

    [EnableIf(typeof(Futari), nameof(Debug))]
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

    #endregion
}