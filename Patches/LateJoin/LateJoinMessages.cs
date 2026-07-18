using UnityEngine.Networking;

namespace MorePlayers.LateJoin
{
    // Custom message ids. Vanilla NetMsgTypes uses 48..~105. Late-join currently
    // claims 1001-1006; spectator couch temporarily uses 1010-1011.
    public static class LateJoinMsgTypes
    {
        public const short Hello = 1001;        // client -> host
        public const short GameState = 1002;    // host -> joiner (unicast)
        public const short Scores = 1003;       // host -> joiner (unicast)
        public const short Activate = 1004;     // relay: server SendToAll, applied everywhere
        public const short PickRequest = 1005;  // client -> host
        public const short PickResult = 1006;   // relay
    }

    public class MsgLateJoinHello : MessageBase
    {
        public int networkNumber;
        public byte requestedMode; // 0 = play, 1 = spectate

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(networkNumber);
            writer.Write(requestedMode);
        }

        public override void Deserialize(NetworkReader reader)
        {
            networkNumber = reader.ReadInt32();
            requestedMode = reader.ReadByte();
        }
    }

    public class MsgLateJoinGameState : MessageBase
    {
        public string sceneName;
        public int phase;
        public int roundNumber;
        public int gameMode;
        public bool partyBox;
        public float placementTimer;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(sceneName ?? string.Empty);
            writer.Write(phase);
            writer.Write(roundNumber);
            writer.Write(gameMode);
            writer.Write(partyBox);
            writer.Write(placementTimer);
        }

        public override void Deserialize(NetworkReader reader)
        {
            sceneName = reader.ReadString();
            phase = reader.ReadInt32();
            roundNumber = reader.ReadInt32();
            gameMode = reader.ReadInt32();
            partyBox = reader.ReadBoolean();
            placementTimer = reader.ReadSingle();
        }
    }

    public class MsgLateJoinScores : MessageBase
    {
        public int[] networkNumbers;
        public int[] totalScores;
        public int[] winStreaks;
        public int[] loseStreaks;
        public bool[] disconnected;

        public override void Serialize(NetworkWriter writer)
        {
            int count = networkNumbers == null ? 0 : networkNumbers.Length;
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(networkNumbers[i]);
                writer.Write(totalScores[i]);
                writer.Write(winStreaks[i]);
                writer.Write(loseStreaks[i]);
                writer.Write(disconnected[i]);
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            int count = reader.ReadInt32();
            networkNumbers = new int[count];
            totalScores = new int[count];
            winStreaks = new int[count];
            loseStreaks = new int[count];
            disconnected = new bool[count];
            for (int i = 0; i < count; i++)
            {
                networkNumbers[i] = reader.ReadInt32();
                totalScores[i] = reader.ReadInt32();
                winStreaks[i] = reader.ReadInt32();
                loseStreaks[i] = reader.ReadInt32();
                disconnected[i] = reader.ReadBoolean();
            }
        }
    }

    public class MsgLateJoinActivate : MessageBase
    {
        public int networkNumber;
        public int animal;
        public int[] outfits;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(networkNumber);
            writer.Write(animal);
            int count = outfits == null ? 0 : outfits.Length;
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(outfits[i]);
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            networkNumber = reader.ReadInt32();
            animal = reader.ReadInt32();
            int count = reader.ReadInt32();
            outfits = new int[count];
            for (int i = 0; i < count; i++)
            {
                outfits[i] = reader.ReadInt32();
            }
        }
    }

    public class MsgLateJoinPickRequest : MessageBase
    {
        public int networkNumber;
        public int animal;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(networkNumber);
            writer.Write(animal);
        }

        public override void Deserialize(NetworkReader reader)
        {
            networkNumber = reader.ReadInt32();
            animal = reader.ReadInt32();
        }
    }

    public class MsgLateJoinPickResult : MessageBase
    {
        public int networkNumber;
        public int animal;
        public bool ok;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(networkNumber);
            writer.Write(animal);
            writer.Write(ok);
        }

        public override void Deserialize(NetworkReader reader)
        {
            networkNumber = reader.ReadInt32();
            animal = reader.ReadInt32();
            ok = reader.ReadBoolean();
        }
    }
}
