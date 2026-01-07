using System;
using System.Collections;
using GameEvent;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Audio;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MorePlayers
{
    class MoepButton
    {
        public const string ButtonName = "MoepButton";
        public static GameObject button;
        public static short MsgNum = (short)(NetMsgTypes.msgCount + 9999);
        public static short MsgSyncNum = (short)(MsgNum + 1);
        public static string MoepText = "MOEP";
        public static System.Collections.Generic.Dictionary<int, int> playerMoepCounts = new System.Collections.Generic.Dictionary<int, int>();
        public static Character touchy;
        public static Texture2D Tex2D;
        public static bool IsButtonCreated = false;
        public static CreditButton creditButtonInstance;

        private static GCHandle? pinnedBank;
        private static uint bankID;

        public class MsgMoepButton : MessageBase
        {
            public int playerNetworkNumber;
            public int currentCount;
        }

        public class MsgMoepSync : MessageBase
        {
            public bool enabled;
            public int[] playerIds;
            public int[] moepCounts;
        }

        public static void LoadEmbeddedBank(string resourceName)
        {
            if (pinnedBank.HasValue) return;

            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Debug.LogError("MoepButton: Could not find embedded resource " + resourceName);
                    return;
                }

                byte[] bankData = new byte[stream.Length];
                stream.Read(bankData, 0, bankData.Length);

                pinnedBank = GCHandle.Alloc(bankData, GCHandleType.Pinned);
                IntPtr pointer = pinnedBank.Value.AddrOfPinnedObject();

                AKRESULT result = AkSoundEngine.LoadBankMemoryView(pointer, (uint)bankData.Length, out bankID);

                if (result == AKRESULT.AK_Success)
                {
                    Debug.Log("MoepButton: Bank loaded successfully from embedded resource: " + resourceName);
                }
                else
                {
                    Debug.LogError("MoepButton: Failed to load bank from memory: " + result);
                    pinnedBank.Value.Free();
                    pinnedBank = null;
                }
            }
        }

        static Texture2D getGlassTex()
        {
            if (Tex2D)
            {
                return Tex2D;
            }

            var assembly = typeof(MorePlayersMod).Assembly;
            var resourceStream = assembly.GetManifestResourceStream("MorePlayers.Patches.assets.glassb.png");

            if (resourceStream != null)
            {
                byte[] linkData = new byte[resourceStream.Length];
                resourceStream.Read(linkData, 0, linkData.Length);

                Tex2D = new Texture2D(2, 2);
                if (Tex2D.LoadImage(linkData))
                    return Tex2D;
            }

            return null;
        }

        public static void Moep(int playerId, int count)
        {
            Debug.Log("MoepButton: Moep called for player " + playerId + " with count " + count);
            playerMoepCounts[playerId] = count;

            // Jetpack reward
            if (count == 5)
            {
                Character target = FindCharacterByPlayerId(playerId);
                if (target != null)
                {
                    target.pickedUpJetpack = true;
                    Debug.Log("MoepButton: Giving jetpack to player " + playerId);
                }
            }

            // Sound
            if (count % 2 == 0)
            {
                AkSoundEngine.PostEvent("moepleft", button);
            }
            else
            {
                AkSoundEngine.PostEvent("moepright", button);
            }
        }

        public static Character FindCharacterByPlayerId(int playerId)
        {
            foreach (Character c in GameObject.FindObjectsOfType<Character>())
            {
                if (c.AssociatedLobbyPlayer != null && c.AssociatedLobbyPlayer.networkNumber == playerId)
                {
                    return c;
                }
            }
            return null;
        }

        static IEnumerator GetRequest()
        {
            using (UnityWebRequest www = UnityWebRequest.Get("https://moepbutton.zmarn.com/"))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("Zmarn Received: " + www.downloadHandler.text);
                    MoepText = www.downloadHandler.text;
                }
                else
                {
                    Debug.Log("Zmarn Error: " + www.error);
                }
            }
        }

        public static void SetupButton(bool enabled)
        {
            if (button != null)
            {
                button.SetActive(enabled);
                return;
            }

            if (enabled && creditButtonInstance != null)
            {
                button = UnityEngine.Object.Instantiate<CreditButton>(creditButtonInstance).gameObject;
                button.SetActive(true);
                button.name = "MoepButton";
                button.transform.localPosition = new Vector3(37.819f, -18.6723f, 0f);
                var tx = button.transform.Find("Canvas (1)/Text").GetComponent<Text>();
                tx.text = "moep";
                tx.color = new Color(0.9434f, 0.9434f, 0.8121f, 0.565f);
                button.transform.Find("Canvas (1)").localPosition = new Vector3(0.03f, -0.5f, 0);

                Texture2D tex = getGlassTex();
                var sprite = Sprite.Create(tex, new Rect(0, 0, 132, 136), new Vector2(0.5f, 0.5f));

                GameObject gl = new GameObject("glass");
                gl.transform.SetParent(button.transform);
                gl.transform.localPosition = new Vector3(0.04f, -1.6f, 0);
                gl.transform.localScale = new Vector3(1.85f, 2.5f, 1f);
                SpriteRenderer SR = gl.AddComponent<SpriteRenderer>();
                SR.sprite = sprite;
                SR.sortingLayerName = "Effects";

                Debug.Log("MoepButton: " + button + " tx.text " + tx.text);

                LoadEmbeddedBank("MorePlayers.Patches.assets.moep.bnk");
            }
        }

        [HarmonyPatch(typeof(CreditButton), nameof(CreditButton.Start))]
        static class MoepButtonPatch
        {
            static void Postfix(CreditButton __instance)
            {
                if (__instance.gameObject.name != "CreditButton") return;
                creditButtonInstance = __instance;

                if (GameSettings.GetInstance().StartAsHost)
                {
                    button = UnityEngine.Object.Instantiate<CreditButton>(__instance).gameObject;
                    button.name = "MoepButton";
                    button.transform.localPosition = new Vector3(37.819f, -18.6723f, 0f);
                    Text component = button.transform.Find("Canvas (1)/Text").GetComponent<Text>();
                    component.text = "moep";
                    component.color = new Color(0.9434f, 0.9434f, 0.8121f, 0.565f);
                    button.transform.Find("Canvas (1)").localPosition = new Vector3(0.03f, -0.5f, 0f);
                    Texture2D glassTex = getGlassTex();
                    Sprite sprite = Sprite.Create(glassTex, new Rect(0f, 0f, 132f, 136f), new Vector2(0.5f, 0.5f));
                    GameObject gameObject = new GameObject("glass");
                    gameObject.transform.SetParent(button.transform);
                    gameObject.transform.localPosition = new Vector3(0.04f, -1.6f, 0f);
                    gameObject.transform.localScale = new Vector3(1.85f, 2.5f, 1f);
                    SpriteRenderer spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                    spriteRenderer.sprite = sprite;
                    spriteRenderer.sortingLayerName = "Effects";

                    //SetupButton(MorePlayersMod.moepButton.Value);
                }

                __instance.StartCoroutine(GetRequest());
            }
        }

        [HarmonyPatch(typeof(CreditButton), nameof(CreditButton.FixedUpdate))]
        static class MoepButtonFixedUpdatePatch
        {
            static bool Prefix(CreditButton __instance)
            {
                if (button != null && __instance.gameObject.name == ButtonName)
                {
                    if (__instance.characterInside)
                    {
                        if (!__instance.characterInsideLastFrame)
                        {
                            __instance.buttonAnimator.SetBool("ButtonPressed", true);
                            MsgMoepButton msg = new MsgMoepButton();
                            if (touchy != null && touchy.AssociatedLobbyPlayer != null)
                            {
                                msg.playerNetworkNumber = touchy.AssociatedLobbyPlayer.networkNumber;
                            }
                            LobbyManager.instance.client.Send(MsgNum, msg);
                        }
                    }
                    else if (__instance.characterInsideLastFrame)
                    {
                        __instance.buttonAnimator.SetBool("ButtonPressed", false);
                    }
                    __instance.characterInsideLastFrame = __instance.characterInside;
                    __instance.characterInside = false;


                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CreditButton), nameof(CreditButton.showCredits))]
        static class MoepButtonShowCreditsPatch
        {
            static bool Prefix(CreditButton __instance)
            {
                if (button != null && __instance.gameObject.name == ButtonName)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CreditButton), nameof(CreditButton.OnTriggerStay2D))]
        static class MoepButtonOnTriggerStay2DPatch
        {
            static void Prefix(CreditButton __instance, Collider2D c)
            {
                CollisionTag component = c.GetComponent<CollisionTag>();
                if (component != null && component.ContainsAnyTag(TagComparer.Tag.Player))
                {
                    Character ca = component.gameObject.GetComponent<Character>();
                    if (ca != null)
                    {
                        touchy = ca;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CreditButton), nameof(CreditButton.handleEvent))]
        static class MoepButtonHandleEventPatch
        {
            static bool Prefix(CreditButton __instance, GameEvent.GameEvent e)
            {
                if (button != null && __instance.gameObject.name == ButtonName)
                {
                    NetworkMessageReceivedEvent networkMessageReceivedEvent = e as NetworkMessageReceivedEvent;
                    if (networkMessageReceivedEvent != null)
                    {
                        if (networkMessageReceivedEvent.Message.msgType == MsgNum)
                        {
                            MsgMoepButton msg = networkMessageReceivedEvent.Message.ReadMessage<MsgMoepButton>();
                            Moep(msg.playerNetworkNumber, msg.currentCount);
                        }
                        else if (networkMessageReceivedEvent.Message.msgType == MsgSyncNum)
                        {
                            MsgMoepSync msg = networkMessageReceivedEvent.Message.ReadMessage<MsgMoepSync>();
                            if (msg.playerIds != null && msg.moepCounts != null)
                            {
                                for (int i = 0; i < msg.playerIds.Length; i++)
                                {
                                    playerMoepCounts[msg.playerIds[i]] = msg.moepCounts[i];
                                }
                            }
                            //SetupButton(msg.enabled);
                        }
                    }

                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.Connect))]
        static class LobbyManagerConnectPatch
        {
            static public void Postfix(LobbyManager __instance)
            {
                NetworkServer.RegisterHandler(MsgNum, new NetworkMessageDelegate(msg =>
                {
                    MsgMoepButton req = msg.ReadMessage<MsgMoepButton>();
                    int pid = req.playerNetworkNumber;
                    if (!playerMoepCounts.ContainsKey(pid)) playerMoepCounts[pid] = 0;
                    playerMoepCounts[pid] += 1;
                    req.currentCount = playerMoepCounts[pid];
                    NetworkServer.SendToAll(MsgNum, req);
                }));
                NetworkServer.RegisterHandler(MsgSyncNum, new NetworkMessageDelegate(__instance.distributeServerMessage));

                if (__instance.client != null)
                {
                    __instance.client.RegisterHandler(MsgNum, new NetworkMessageDelegate(__instance.distributeMessage));
                    __instance.client.RegisterHandler(MsgSyncNum, new NetworkMessageDelegate(__instance.distributeMessage));
                }
            }
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnServerAddPlayer))]
        static class LobbyManagerOnServerAddPlayerPatch
        {
            static public void Postfix(LobbyManager __instance, NetworkConnection conn, short playerControllerId)
            {
                MsgMoepSync msg = new MsgMoepSync();
                msg.enabled = MorePlayersMod.moepButton.Value;

                int count = playerMoepCounts.Count;
                msg.playerIds = new int[count];
                msg.moepCounts = new int[count];
                int i = 0;
                foreach (var kvp in playerMoepCounts)
                {
                    msg.playerIds[i] = kvp.Key;
                    msg.moepCounts[i] = kvp.Value;
                    i++;
                }

                conn.Send(MsgSyncNum, msg);
            }
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.readMessage))]
        static class LobbyManagerReadMessagePatch
        {
            static public bool Prefix(LobbyManager __instance, NetworkMessage msg, ref MessageBase __result)
            {
                if (msg.msgType == MsgNum)
                {
                    __result = msg.ReadMessage<MsgMoepButton>();
                    return false;
                }
                if (msg.msgType == MsgSyncNum)
                {
                    __result = msg.ReadMessage<MsgMoepSync>();
                    return false;
                }
                return true;
            }
        }
    }
}