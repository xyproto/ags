using AGS.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AGS.Editor
{
    internal class RoomLoader
    {
        // TODO: Make sure that this all does what it's supposed to.
        //       I'm not sure of all of it. Especially the image reading bits. -monkey

        // TODO: Split the methods here into other classes and files!
        //       I'm keeping it localized for now to make sure everything fits together
        //       and hopefully works before I distribute the code appropriately.
        //       Porting from native has not been a joke, it's hard work! :) -monkey

        // TODO: A lot of this is duplicates of what's already in the Room class, get it out of here!
        private static InteractionSchema _interactionSchema;
        private List<RoomBackground> _backgrounds = new List<RoomBackground>();
        private Bitmap _hotspotMask;
        private int _hotspotCount; // TODO: phase out these superfluous variables
        private List<RoomHotspot> _hotspots = new List<RoomHotspot>();
        private int _objectCount;
        private List<RoomObject> _objects = new List<RoomObject>();
        private Bitmap _regionMask;
        private int _regionCount;
        private List<RoomRegion> _regions = new List<RoomRegion>();
        private Bitmap _walkableAreaMask;
        private int _walkableAreaCount;
        private List<RoomWalkableArea> _walkableAreas = new List<RoomWalkableArea>();
        private Bitmap _walkBehindMask;
        private int _walkBehindCount;
        private List<RoomWalkBehind> _walkBehinds = new List<RoomWalkBehind>();
        private int _messageCount;
        private List<string> _messages = new List<string>();
        private List<MessageInfo> _messageInfos = new List<MessageInfo>();
        private RoomFileVersion _loadedVersion;
        private int _bpp;
        private int _edgeTop;
        private int _edgeBottom;
        private int _edgeLeft;
        private int _edgeRight;
        private int _localVariableCount;
        private List<OldInteractionVariable> _localVariables = new List<OldInteractionVariable>();
        private Interactions _interactions = new Interactions(_interactionSchema);
        private int _width;
        private int _height;
        private RoomResolution _resolution;
        private bool _stateSaving;
        private int[] _options = new int[10];
        private int _gameID;
        private List<Color> _palette = new List<Color>();

        static RoomLoader()
        {
            // TODO: duplicate of what's in the Room class, just use that instead
            _interactionSchema = new InteractionSchema(
                new string[]
                {
                    "Walks off left edge",
                    "Walks off right edge",
                    "Walks off bottom edge",
                    "Walks off top edge",
                    "First time enters room",
                    "Enters room before fade-in",
                    "Repeatedly execute",
                    "Enters room after fade-in",
                    "Leaves room"
                },
                new string[]
                {
                    "LeaveLeft",
                    "LeaveRight",
                    "LeaveBottom",
                    "LeaveTop",
                    "FirstLoad",
                    "Load",
                    "RepExec",
                    "AfterFadein",
                    "Leave"
                });
        }

        public class RoomBackground
        {
            public Bitmap Graphic
            {
                get;
                set;
            }
        }

        public enum RoomLoadError
        {
            NoError,
            InternalLogicError,
            FormatNotSupported,
            UnexpectedEOF,
            OldBlockNotSupported,
            UnknownBlockType,
        };

        enum RoomFileVersion
        {
            Undefined = 0,
            Pre114Version3 = 3,  // exact version unknown
            Pre114Version4 = 4,  // exact version unknown
            Pre114Version5 = 5,  // exact version unknown
            Pre114Version6 = 6,  // exact version unknown
            Version114 = 8,
            Version200Alpha = 9,
            Version200Alpha7 = 10,
            Version200Final = 11,
            Version208 = 12,
            Version214 = 13,
            Version240 = 14,
            Version241 = 15,
            Version250A = 16,
            Version250B = 17,
            Version251 = 18,
            Version253 = 19,
            Version255A = 20,
            Version255B = 21,
            Version261 = 22,
            Version262 = 23,
            Version270 = 24,
            Version272 = 25,
            Version300A = 26,
            Version300B = 27,
            Version303A = 28,
            Version303B = 29,
            Version340Alpha,
            Current = Version340Alpha
        };

        private void Free()
        {
            _backgrounds.Clear();
            _hotspotMask = null;
            _regionMask = null;
            _walkableAreaMask = null;
            _walkBehindMask = null;
            _messages.Clear();
        }

        private void InitializeDefaults()
        {
            _loadedVersion = RoomFileVersion.Current;
            _backgrounds.Clear();
            _backgrounds.Add(new RoomBackground());
        }

        /**\
         * void RoomInfo::InitDefaults()
{
    LoadedVersion = kRoomVersion_Current;
    GameId = NO_GAME_ID_IN_ROOM_FILE;

    Width           = 320;
    Height          = 200;
    Edges.Left      = 0;
    Edges.Right     = 317;
    Edges.Top       = 40;
    Edges.Bottom    = 199;
    Resolution      = 1;
    BytesPerPixel   = 1;

    HotspotCount    = 0;
    ObjectCount     = 0;
    RegionCount     = 0;
    WalkAreaCount   = 0;
    WalkBehindCount = 0;
    BkgSceneCount   = 1;
    LocalVariableCount = 0;
    MessageCount    = 0;
    AnimationCount  = 0;

    Backgrounds.New(1);
    BkgSceneAnimSpeed = 5;

    CompiledScriptShared = false;
    CompiledScriptSize = 0;

    memset(Palette, 0, sizeof(Palette));
    IsPersistent = false;
    memset(Options, 0, sizeof(Options));
}
         */

        enum RoomFormatBlock
        {
            None = 0,
            Main = 1,
            Script = 2,
            CompiledScript = 3,
            CompiledScript2 = 4,
            ObjectNames = 5,
            AnimatedBackground = 6,
            CompiledScript3 = 7,
            Properties = 8,
            ObjectScriptNames = 9,
            End = 0xFF
        };

        #region OldInteraction crap
        // TODO: Ideally, optimize most of these classes out entirely.
        //       They are only here now for reading legacy room files. -monkey

        private class OldInteractionValue
        {
            public enum ValueType
            {
                Int,
                Variable,
                Boolean,
                CharacterID
            };

            public ValueType Type
            {
                get;
                set;
            }

            public int Value
            {
                get;
                set;
            }

            public int Extra
            {
                get;
                set;
            }

            public OldInteractionValue()
            {
                Type = ValueType.Int;
                Value = 0;
                Extra = 0;
            }

            public void ReadFromFile(BinaryReader reader, bool aligned)
            {
                Type = (ValueType)reader.ReadByte();
                if (aligned) reader.ReadBytes(sizeof(int) - sizeof(byte)); // read padding to align to 32-bit boundary
                Value = reader.ReadInt32();
                Extra = reader.ReadInt32();
            }
        }

        private class OldInteractionCommandList
        {
            public const int MAX_COMMANDS_PER_LIST = 40;

            public int CommandCount
            {
                get;
                set;
            }

            public OldInteractionCommand[] Commands
            {
                get;
                private set;
            }

            public int TimesRun
            {
                get;
                set;
            }

            public OldInteractionCommandList()
            {
                CommandCount = 0;
                TimesRun = 0;
                Commands = new OldInteractionCommand[MAX_COMMANDS_PER_LIST];
            }

            public OldInteractionCommandList(OldInteractionCommandList other) : this()
            {
                CommandCount = other.CommandCount;
                TimesRun = other.TimesRun;
                for (int i = 0; i < other.CommandCount; ++i)
                {
                    Commands[i].Assign(other.Commands[i], this);
                }
            }

            public void Reset()
            {
                for (int i = 0; i < CommandCount; ++i)
                {
                    Commands[i].Reset();
                }
                CommandCount = 0;
                TimesRun = 0;
            }

            public void ReadCommandsFromFile(BinaryReader reader)
            {
                for (int i = 0; i < CommandCount; ++i)
                {
                    Commands[i].ReadFromFile(reader);
                }
            }
        }

        private class OldInteractionCommand
        {
            public const int MAX_ACTION_ARGS = 5;

            public enum CommandType
            {
                Nothing = 0,
                RunScript = 1,
                AddScoreOnFirstExecution = 2,
                AddScore = 3,
                DisplayMessage = 4,
                PlayMusic = 5,
                StopMusic = 6,
                PlaySound = 7,
                PlayFLIC = 8,
                RunDialog = 9,
                EnableDialogOption = 10,
                DisableDialogOption = 11,
                ChangeRoomAutoPosition = 12,
                GivePlayerInventoryItem = 13,
                MoveObject = 14,
                HideObject = 15,
                ShowObject = 16,
                ChangeObjectView = 17,
                AnimateObject = 18,
                MoveCharacter = 19,
                IfInventoryItemWasUsed = 20,
                IfPlayerHasInventoryItem = 21,
                IfPlayerIsMoving = 22,
                IfVariableSetToValue = 23,
                StopMoving = 24,
                ChangeRoomAtCoords = 25,
                ChangeRoomOfNPC = 26,
                LockCharacterView = 27,
                UnlockCharacterView = 28,
                FollowCharacter = 29,
                StopFollowing = 30,
                DisableHotspot = 31,
                EnableHotspot = 32,
                SetVariable = 33,
                AnimateCharacter = 34,
                QuickCharacterAnimation = 35,
                SetIdle = 36,
                DisableIdle = 37,
                LoseInventory = 38,
                ShowGUI = 39,
                HideGUI = 40,
                StopRunningCommands = 41,
                FaceLocation = 42,
                Wait = 43,
                ChangeCharacterView = 44,
                IfPlayerSetTo = 45,
                IfMouseCursorSetTo = 46,
                IfPlayerHasBeenInRoom = 47
            };

            public CommandType Type
            {
                get;
                set;
            }

            public OldInteractionValue[] Data
            {
                get;
                private set;
            }

            public OldInteractionCommandList Children
            {
                get;
                set;
            }

            public OldInteractionCommandList Parent
            {
                get;
                set;
            }

            public OldInteractionCommand()
            {
                Type = 0;
                Data = new OldInteractionValue[MAX_ACTION_ARGS];
                Children = null;
                Parent = null;
            }

            public void Reset()
            {
                Children = null;
                Parent = null;
                Type = 0;
                for (int i = 0; i < Data.Length; ++i)
                {
                    Data[i] = null;
                }
            }

            public void Assign(OldInteractionCommand cmd, OldInteractionCommandList parent)
            {
                Type = cmd.Type;
                for (int i = 0; i < Data.Length; ++i)
                {
                    Data[i] = cmd.Data[i];
                }
                Children = (cmd.Children == null) ? new OldInteractionCommandList() : null;
                Parent = parent;
            }

            public void ReadFromFile(BinaryReader reader)
            {
                reader.ReadInt32(); // skip the vtbl ptr
                Type = (OldInteractionCommand.CommandType)reader.ReadInt32();
                for (int i = 0; i < Data.Length; ++i)
                {
                    Data[i].ReadFromFile(reader, true);
                }
                // all that matters here is if these are null or not
                Children = ((long)reader.ReadInt32() == 0L) ? null : new OldInteractionCommandList();
                Parent = ((long)reader.ReadInt32() == 0L) ? null : new OldInteractionCommandList();
            }
        }

        private class OldInteraction
        {
            public const int MAX_EVENTS = 30;

            public int EventCount
            {
                get;
                set;
            }

            public int[] EventTypes
            {
                get;
                private set;
            }

            public int[] TimesRun
            {
                get;
                private set;
            }

            public OldInteractionCommandList[] Response
            {
                get;
                private set;
            }

            public OldInteraction()
            {
                EventCount = 0;
                EventTypes = new int[MAX_EVENTS];
                TimesRun = new int[MAX_EVENTS];
                Response = new OldInteractionCommandList[MAX_EVENTS];
            }

            public OldInteraction(OldInteraction other)
            {
                EventCount = other.EventCount;
                Array.Copy(other.EventTypes, EventTypes, EventTypes.Length);
                Array.Copy(other.TimesRun, TimesRun, TimesRun.Length);
                for (int i = 0; i < EventCount; ++i)
                {
                    if (other.Response[i] != null)
                    {
                        Response[i] = new OldInteractionCommandList(other.Response[i]);
                    }
                }
            }

            public void CopyTimesRunFrom(OldInteraction other)
            {
                Array.Copy(other.TimesRun, TimesRun, TimesRun.Length);
            }

            public void Reset()
            {
                EventCount = 0;
                for (int i = 0; i < MAX_EVENTS; ++i)
                {
                    EventTypes[i] = 0;
                    TimesRun[i] = 0;
                    Response[i] = null;
                }
            }

            public void ReadFromFile(BinaryReader reader, bool ignorePointers)
            {
                EventCount = reader.ReadInt32();
                // reading these as byte arrays allows us to collapse several loops down to just one
                byte[] eventBytes = reader.ReadBytes(sizeof(int) * EventTypes.Length);
                byte[] timesRunBytes = reader.ReadBytes(sizeof(int) * TimesRun.Length);
                for (int i = 0; i < MAX_EVENTS; ++i)
                {
                    EventTypes[i] = BitConverter.ToInt32(eventBytes, i * sizeof(int));
                    TimesRun[i] = BitConverter.ToInt32(timesRunBytes, i * sizeof(int));
                    if (ignorePointers) reader.ReadInt32();
                    else Response[i] = (reader.ReadInt32() != 0) ? new OldInteractionCommandList() : null;
                }
            }
        }

        private class OldInteractionAction
        {
            public const int NUM_ACTION_TYPES = 48;
            public const int MAX_ACTION_ARGS = 5;

            public enum ActionArgument
            {
                Int = 1,
                InventoryItemID = 2,
                Message = 3,
                CharacterID = 4,
                Bool = 5,
                GraphicalVariableName = 6
            };

            public enum ActionFlags
            {
                Conditional = 1,
                RunScript = 2,
                CheckInventory = 4,
                Message = 8
            };

            public string Name
            {
                get;
                set;
            }

            public ActionFlags Flags
            {
                get;
                set;
            }

            public int ArgumentCount
            {
                get;
                set;
            }

            public ActionArgument[] ArgumentTypes
            {
                get;
                private set;
            }

            public string[] ArgumentNames
            {
                get;
                private set;
            }

            public string Description
            {
                get;
                set;
            }

            public string TextScript
            {
                get;
                set;
            }

            public OldInteractionAction()
            {
                ArgumentTypes = new ActionArgument[MAX_ACTION_ARGS];
                ArgumentNames = new string[MAX_ACTION_ARGS];
            }

            public OldInteractionAction(string name, ActionFlags flags, int argCount, ActionArgument[] argTypes, string[] argNames, string description, string textScript) : this()
            {
                Name = name;
                Flags = flags;
                ArgumentCount = argCount;
                if (ArgumentCount > 0)
                {
                    Array.Copy(argTypes, ArgumentTypes, argCount);
                    Array.Copy(argNames, ArgumentNames, argCount);
                }
                Description = description;
                TextScript = textScript;
            }

            public static OldInteractionAction[] Actions
            {
                get;
                private set;
            }

            static OldInteractionAction()
            {
                Actions = new OldInteractionAction[NUM_ACTION_TYPES]
                {
                    // TODO: Get rid of superfluous crap... ;)
                    //       On an unrelated note, this is all deprecated but I included it (ALL)
                    //       here in case it's needed for anything else. I'll leave it in at least
                    //       one git commit for legacy purposes. -monkey
                    new OldInteractionAction("Do nothing", 0, 0, null, null, "Does nothing.", ""),
                    new OldInteractionAction("Run script", ActionFlags.RunScript, 0, null, null, "Runs a text script. Click the 'Edit Script button to modify the script.", ""),
                    new OldInteractionAction("Game - Add score on first execution", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Points to add" }, "Gives the player $$1 extra points the first time this action is run.", ""),
                    new OldInteractionAction("Game - Add score", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Points to add" }, "Gives the player $$1 extra points every time this action is run.", "GiveScore($$1);"),
                    new OldInteractionAction("Game - Display a message", ActionFlags.Message, 1, new ActionArgument[] { ActionArgument.Message }, new string[] { "Message number" }, "Displays message $$1 to the player.", "DisplayMessage($$1);"),
                    new OldInteractionAction("Game - Play music", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Music number" }, "Changes the current background music to MUSIC$$1.MP3, WAV, MID or MOD", "PlayMusic($$1);"),
                    new OldInteractionAction("Game - Stop music", 0, 0, null, null, "Stops the currently playing background music.", "StopMusic();"),
                    new OldInteractionAction("Game - Play sound effect", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Sound number" }, "Plays SOUND$$1.MP3 or SOUND$$1.WAV", "PlaySound($$1);"),
                    new OldInteractionAction("Game - Play Flic animation", 0, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Bool }, new string[] { "Flic number", "Player can skip" }, "Plays FLIC$$1.FLC or FLIC$$1.FLI", "PlayFlic($$1, $$2);"),
                    new OldInteractionAction("Game - Run dialog", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Dialog topic number" }, "Starts a conversation using dialog topic $$1.", "dialog[$$1].Start();"),
                    new OldInteractionAction("Game - Enable dialog option", 0, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int }, new string[] { "Dialog topic number", "Option number" }, "Enables dialog option $$2 in topic $$1 to be visible to the player.", "dialog[$$1].SetOptionState($$2, eOptionOn);"),
                    new OldInteractionAction("Game - Disable dialog option", 0, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int }, new string[] { "Dialog topic number", "Option number" }, "Stops dialog option $$2 in topic $$1 from being visible to the player.", "dialog[$$1].SetOptionState($$2, eOptionOff);"),
                    new OldInteractionAction("Player - Go to a different room", 0, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int }, new string[] { "New room number", "Edge+offset value" }, "Takes the player to room $$1, optionally choosing position using old-style Data column value $$2", "player.ChangeRoom($$1);"),
                    new OldInteractionAction("Player - Give the player an inventory item", 0, 1, new ActionArgument[] { ActionArgument.InventoryItemID }, new string[] { "Inventory item number" }, "Adds inventory item $$1 to the player character's inventory.", "player.AddInventory(inventory[$$1]);"),
                    new OldInteractionAction("Object - Move object", 0, 5, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int, ActionArgument.Int, ActionArgument.Int, ActionArgument.Bool }, new string[] { "Object number", "Destination X location", "Destination Y location", "Move speed", "Wait for move to finish" }, "Starts object $$1 moving towards ($$2, $$3), at speed $$4.", "object[$$1].Move($$2, $$3, $$4);"),
                    new OldInteractionAction("Object - Remove an object from the room", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Object number" }, "Switches room object $$1 off so the player cannot see or interact with it.", "object[$$1].Visible = false;"),
                    new OldInteractionAction("Object - Switch an object back on", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Object number" }, "Switches room object $$1 on, so that the player can see and interact with it.", "object[$$1].Visible = true;"),
                    new OldInteractionAction("Object - Set object view number", 0, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int }, new string[] { "Object number", "New view number" }, "Changes object $$1's view number to $$2", "object[$$1].SetView($$2);"),
                    new OldInteractionAction("Object - Start object animating", 0, 4, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int, ActionArgument.Int, ActionArgument.Bool }, new string[] { "Object number", "Loop number", "Speed", "Repeat" }, "Starts object $$1 animating, using loop $$2 of its current view, and animating at speed $$3.", "object[$$1].Animate($$2, $$3);"),
                    new OldInteractionAction("Character - Move character", 0, 4, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int, ActionArgument.Int, ActionArgument.Bool }, new string[] { "Character", "Destination X location", "Destination Y location", "Wait for move to finish" }, "Starts character $$1 moving towards ($$2, $$3).", "character[$$1].Walk($$2, $$3);"),
                    new OldInteractionAction("Conditional - If inventory item was used", ActionFlags.Conditional | ActionFlags.CheckInventory, 1, new ActionArgument[] { ActionArgument.InventoryItemID }, new string[] { "Inventory item number" }, "Performs child actions if the player just used inventory item $$1 on this interaction.", "if (player.ActiveInventory == inventory[$$1]) {"),
                    new OldInteractionAction("Conditional - If the player has an inventory item", ActionFlags.Conditional, 1, new ActionArgument[] { ActionArgument.InventoryItemID }, new string[] { "Inventory item number" }, "Performs child actions if the player character has inventory item $$1 in their current inventory.", "if (player.InventoryQuantity[$$1] > 0) {"),
                    new OldInteractionAction("Conditional - If a character is moving", ActionFlags.Conditional, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character number" }, "Performs child actions if character $$1 is currently moving", "if (character[$$1].Moving) {"),
                    new OldInteractionAction("Conditional - If a variable is set to a certain value", ActionFlags.Conditional, 2, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int }, new string[] { "Variable", "Value" }, "Performs child actions if $$1 == $$2", "if (GetGraphicalVariable(\"$$1\") == $$2) { "),
                    new OldInteractionAction("Character - Stop character walking", 0, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character" }, "Immediately stops character $$1 from moving.", "character[$$1].StopMoving();"),
                    new OldInteractionAction("Player - Go to a different room (at specific co-ordinates)", 0, 3, new ActionArgument[] { ActionArgument.Int, ActionArgument.Int, ActionArgument.Int }, new string[] { "New room number", "X co-ordinate", "Y co-ordinate" }, "Takes the player to room $$1, and places him at ($$2, $$3)", "player.ChangeRoom($$1, $$2, $$3);"),
                    new OldInteractionAction("Character - Move NPC to different room", 0, 2, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int }, new string[] { "Character", "New room number" }, "Places non-player character $$1 into room $$2.", "character[$$1].ChangeRoom($$2);"),
                    new OldInteractionAction("Character - Set character view", 0, 2, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int }, new string[] { "Character", "View number" }, "Locks character $$1's view to view $$2, in preparation for doing an animation. Use 'Release Character View' afterwards to release.", "character[$$1].LockView($$2);"),
                    new OldInteractionAction("Character - Release character view", 0, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character" }, "Reverts character $$1's view back to its normal view and enables standard engine processing.", "character[$$1].UnlockView();"),
                    new OldInteractionAction("Character - Follow another character", 0, 2, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.CharacterID }, new string[] { "Character", "Follow Character" }, "Makes character $$1 follow $$2 around the screen.", "character[$$1].FollowCharacter($$2);"),
                    new OldInteractionAction("Character - Stop following", 0, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character" }, "Stops character $$1 following any other characters.", "character[$$1].FollowCharacter(null);"),
                    new OldInteractionAction("Room - Disable hotspot", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Hotspot number" }, "Disables hotspot $$1 in current room.", "hotspot[$$1].Enabled = false;"),
                    new OldInteractionAction("Room - Enable hotspot", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Hotspot number" }, "Re-enables hotspot $$1 in the current room.", "hotspot[$$1].Enabled = true;"),
                    new OldInteractionAction("Game - Set variable value", 0, 2, new ActionArgument[] { ActionArgument.GraphicalVariableName, ActionArgument.Int }, new string[] { "Variable", "New value" }, "Sets variable $$1 to have the value $$2", "SetGraphicalVariable(\"$$1\", $$2);"),
                    new OldInteractionAction("Character - Run animation loop", 0, 3, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int, ActionArgument.Int }, new string[] { "Character", "Loop number", "Speed" }, "Runs character $$1 through loop $$2 of its current view, animating at speed $$3. Waits for animation to finish before continuing.", ""),
                    new OldInteractionAction("Character - Quick animation", 0, 4, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int, ActionArgument.Int, ActionArgument.Int }, new string[] { "Character", "View number", "Loop number", "Speed" }, "Does SetCharacterView($$1, $$2), AnimateCharacter($$1, $$3, $$4), ReleaseCharacterView($$1) in order.", ""),
                    new OldInteractionAction("Character - Set idle animation", 0, 3, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int, ActionArgument.Int }, new string[] { "Character", "View number", "Delay" }, "Sets character $$1 to use view $$2 as its idle animation, with a timeout of $$3 seconds of inactivity before the animation is played.", "character[$$1].SetIdleView($$2, $$3);"),
                    new OldInteractionAction("Character - Disable idle animation", 0, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character" }, "Disables character $$1's idle animation, so it will no longer be played.", "character[$$1].SetIdleView(-1, -1);"),
                    new OldInteractionAction("Player - Remove an item from the inventory", 0, 1, new ActionArgument[] { ActionArgument.InventoryItemID }, new string[] { "Inventory item number" }, "Removes inventory item $$1 from the player character's inventory.", "player.LoseInventory(inventory[$$1]);"),
                    new OldInteractionAction("Game - Show GUI", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "GUI number" }, "Switches on GUI number $$1 so the player can see it.", "gui[$$1].Visible = true;"),
                    new OldInteractionAction("Game - Hide GUI", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "GUI number" }, "Switches off GUI number $$1 so the player can no longer see it.", "gui[$$1].Visible = false;"),
                    new OldInteractionAction("Stop running more commands", 0, 0, null, null, "Stops running the interaction list at this point. Useful at the end of a block of actions inside a conditional.", "return;"),
                    new OldInteractionAction("Character - Face location", 0, 3, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int, ActionArgument.Int }, new string[] { "Character", "X co-ordinate", "Y co-ordinate" }, "Turns character $$1 so that they are facing the room co-ordinates ($$2, $$3).", "character[$$1].FaceLocation($$2, $$3);"),
                    new OldInteractionAction("Game - Pause command processor for a set time", 0, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Loops to wait" }, "Stops processing actions here and lets the game continue execution for $$1 game loops (default 40 per second) before continuing with the next command.", "Wait($$1);"),
                    new OldInteractionAction("Character - Change character view", 0, 2, new ActionArgument[] { ActionArgument.CharacterID, ActionArgument.Int }, new string[] { "Character", "New view number" }, "Changes character $$1's normal walking view to View $$2 permanently, until you call this command again.", "character[$$1].ChangeView($$2);"),
                    new OldInteractionAction("Conditional - If the player character is", ActionFlags.Conditional, 1, new ActionArgument[] { ActionArgument.CharacterID }, new string[] { "Character" }, "Performs child actions if the player character is currently $$1. Useful in games where the player can control multiple characters.", "if (player.ID == $$1) {"),
                    new OldInteractionAction("Conditional - If mouse cursor mode is", ActionFlags.Conditional, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Mouse cursor" }, "Performs child actions if the current mode is mode $$1 (from the Cursors pane).", "if (mouse.Mode == $$1) {"),
                    new OldInteractionAction("Conditional - If the player has been in room", ActionFlags.Conditional, 1, new ActionArgument[] { ActionArgument.Int }, new string[] { "Room number" }, "Performs child actions if the player has been to room $$1 during the game.", "if (HasPlayerBeenInRoom($$1)) {")
                };
            }
        }

        void ConvertInteractionToScript(StringBuilder sb, OldInteractionCommand cmd, String scriptFuncPrefix, Game game, ref int runScriptCount, ref bool onlyIfInvWasUsed, int commandOffset)
        {
            if (cmd.Type != OldInteractionCommand.CommandType.RunScript)
            {
                // if another type of interaction, we definitely can't optimize away the wrapper
                runScriptCount = 1000;
            }
            else runScriptCount++;
            if (cmd.Type != OldInteractionCommand.CommandType.IfInventoryItemWasUsed)
            {
                onlyIfInvWasUsed = false;
            }
            switch (cmd.Type)
            {
                case OldInteractionCommand.CommandType.Nothing:
                    break;
                case OldInteractionCommand.CommandType.RunScript: // Run script
                    {
                        sb.Append(scriptFuncPrefix);
                        sb.Append(System.Convert.ToChar(cmd.Data[0].Value + 'a'));
                        sb.Append("();");
                    }
                    break;
                case OldInteractionCommand.CommandType.AddScore: // Add score
                case OldInteractionCommand.CommandType.DisplayMessage: // Display message
                case OldInteractionCommand.CommandType.PlayMusic: // Play music
                case OldInteractionCommand.CommandType.StopMusic: // Stop music
                case OldInteractionCommand.CommandType.PlaySound: // Play sound
                case OldInteractionCommand.CommandType.PlayFLIC: // Play FLIC
                case OldInteractionCommand.CommandType.RunDialog: // Run dialog
                case OldInteractionCommand.CommandType.EnableDialogOption: // Enable dialog option
                case OldInteractionCommand.CommandType.DisableDialogOption: // Disable dialog option
                case OldInteractionCommand.CommandType.GivePlayerInventoryItem: // Give player an inventory item
                case OldInteractionCommand.CommandType.HideObject: // Hide object
                case OldInteractionCommand.CommandType.ShowObject: // Show object
                case OldInteractionCommand.CommandType.ChangeObjectView: // Change object view
                case OldInteractionCommand.CommandType.IfInventoryItemWasUsed: // If inventory item was used
                case OldInteractionCommand.CommandType.IfPlayerHasInventoryItem: // If player has inventory item
                case OldInteractionCommand.CommandType.IfPlayerIsMoving: // If player is moving
                case OldInteractionCommand.CommandType.StopMoving: // Stop moving
                case OldInteractionCommand.CommandType.ChangeRoomAtCoords: // Change room (at coords)
                case OldInteractionCommand.CommandType.ChangeRoomOfNPC: // Change room of NPC
                case OldInteractionCommand.CommandType.LockCharacterView: // Lock character view
                case OldInteractionCommand.CommandType.UnlockCharacterView: // Unlock character view
                case OldInteractionCommand.CommandType.FollowCharacter: // Follow character
                case OldInteractionCommand.CommandType.StopFollowing: // Stop following
                case OldInteractionCommand.CommandType.DisableHotspot: // Disable hotspot
                case OldInteractionCommand.CommandType.EnableHotspot: // Enable hotspot
                case OldInteractionCommand.CommandType.SetIdle: // Set idle
                case OldInteractionCommand.CommandType.DisableIdle: // Disable idle
                case OldInteractionCommand.CommandType.LoseInventory: // Lose inventory
                case OldInteractionCommand.CommandType.ShowGUI: // Show GUI
                case OldInteractionCommand.CommandType.HideGUI: // Hide GUI
                case OldInteractionCommand.CommandType.StopRunningCommands: // Stop running commands
                case OldInteractionCommand.CommandType.FaceLocation: // Face location
                case OldInteractionCommand.CommandType.Wait: // Wait
                case OldInteractionCommand.CommandType.ChangeCharacterView: // Change character's view
                case OldInteractionCommand.CommandType.IfPlayerSetTo: // If player is
                case OldInteractionCommand.CommandType.IfMouseCursorSetTo: // If mouse cursor is
                case OldInteractionCommand.CommandType.IfPlayerHasBeenInRoom: // If player has been in room
                    // For these, the sample script code will work
                    {
                        string scriptCode = OldInteractionAction.Actions[(int)cmd.Type].TextScript;
                        if ((onlyIfInvWasUsed) && (commandOffset > 0)) scriptCode = "else " + scriptCode;
                        scriptCode = scriptCode.Replace("$$1", cmd.Data[0].Value.ToString());
                        scriptCode = scriptCode.Replace("$$2", cmd.Data[1].Value.ToString());
                        scriptCode = scriptCode.Replace("$$3", cmd.Data[2].Value.ToString());
                        scriptCode = scriptCode.Replace("$$4", cmd.Data[3].Value.ToString());
                        sb.AppendLine(scriptCode);
                    }
                    break;
                case OldInteractionCommand.CommandType.AnimateCharacter: // Animate character
                    {
                        int charID = cmd.Data[0].Value;
                        int loop = cmd.Data[1].Value;
                        int speed = cmd.Data[2].Value;
                        string charName = "character[" + charID.ToString() + "]";
                        if ((game != null) && (charID >= 0) && (charID < game.Characters.Count) &&
                            (!string.IsNullOrEmpty(game.Characters[charID].ScriptName)))
                        {
                            charName = game.Characters[charID].ScriptName;
                        }
                        sb.AppendLine(string.Format("{0}.Animate({1}, {2}, eOnce, eBlock);", charName, loop, speed));
                    }
                    break;
                case OldInteractionCommand.CommandType.QuickCharacterAnimation: // Quick animation
                    {
                        int charID = cmd.Data[0].Value;
                        int view = cmd.Data[1].Value;
                        int loop = cmd.Data[2].Value;
                        int speed = cmd.Data[3].Value;
                        string charName = "character[" + charID.ToString() + "]";
                        if ((game != null) && (charID >= 0) && (charID < game.Characters.Count) &&
                            (!string.IsNullOrEmpty(game.Characters[charID].ScriptName)))
                        {
                            charName = game.Characters[charID].ScriptName;
                        }
                        // TODO: Verify that string.Format works how I think it does here... -monkey
                        sb.AppendLine(string.Format("{0}.LockView({1});\n" +
                                                    "{0}.Animate({2}, {3}, eOnce, eBlock);\n" +
                                                    "{0}.UnlockView();", charName, view, loop, speed));
                    }
                    break;
                case OldInteractionCommand.CommandType.MoveObject: // Move object
                    {
                        int objID = cmd.Data[0].Value;
                        int x = cmd.Data[1].Value;
                        int y = cmd.Data[2].Value;
                        int speed = cmd.Data[3].Value;
                        sb.AppendLine(string.Format("object[{0}].Move({1}, {2}, {3}, {4});", objID, x, y, speed, (cmd.Data[4].Value == 0) ? "eNoBlock" : "eBlock"));
                    }
                    break;
                case OldInteractionCommand.CommandType.MoveCharacter: // Move character
                    {
                        int charID = cmd.Data[0].Value;
                        int x = cmd.Data[1].Value;
                        int y = cmd.Data[2].Value;
                        string charName = "character[" + charID.ToString() + "]";
                        if ((game != null) && (charID >= 0) && (charID < game.Characters.Count) &&
                            (!string.IsNullOrEmpty(game.Characters[charID].ScriptName)))
                        {
                            charName = game.Characters[charID].ScriptName;
                        }
                        sb.AppendLine(string.Format("{0}.Walk({1}, {2}, {3});", charName, x, y, (cmd.Data[3].Value == 0) ? "eNoBlock" : "eBlock"));
                    }
                    break;
                case OldInteractionCommand.CommandType.AnimateObject: // Animate object
                    {
                        int objID = cmd.Data[0].Value;
                        int loop = cmd.Data[1].Value;
                        int speed = cmd.Data[2].Value;
                        sb.AppendLine(string.Format("object[{0}].Animate({1}, {2}, {3}, eNoBlock);", objID, loop, speed, (cmd.Data[3].Value == 0) ? "eOnce" : "eRepeat"));
                    }
                    break;
                case OldInteractionCommand.CommandType.IfVariableSetToValue: // If variable set to value
                    {
                        int valueToCheck = cmd.Data[1].Value;
                        string scriptCode = null;
                        if ((game == null) || (cmd.Data[0].Value >= game.OldInteractionVariables.Count))
                        {
                            scriptCode = string.Format("if (__INTRVAL${0}$ == {1}) {", cmd.Data[0].Value, valueToCheck);
                        }
                        else
                        {
                            OldInteractionVariable variableToCheck = game.OldInteractionVariables[cmd.Data[0].Value];
                            scriptCode = string.Format("if ({0} == {1}) {", variableToCheck.ScriptName, valueToCheck);
                        }
                        sb.AppendLine(scriptCode);
                    }
                    break;
                case OldInteractionCommand.CommandType.SetVariable: // Set variable
                    {
                        int valueToSet = cmd.Data[1].Value;
                        string scriptCode = null;
                        if ((game == null) || (cmd.Data[0].Value >= game.OldInteractionVariables.Count))
                        {
                            scriptCode = string.Format("__INTRVAL${0}$ = {1};", cmd.Data[0].Value, valueToSet);
                        }
                        else
                        {
                            OldInteractionVariable variableToCheck = game.OldInteractionVariables[cmd.Data[0].Value];
                            scriptCode = string.Format("{0} = {1};", variableToCheck.ScriptName, valueToSet);
                        }
                        sb.AppendLine(scriptCode);
                    }
                    break;
                case OldInteractionCommand.CommandType.ChangeRoomAutoPosition: // Change room
                    {
                        int room = cmd.Data[0].Value;
                        string scriptCode = "player.ChangeRoomAutoPosition(" + room.ToString() +
                            ((cmd.Data[1].Value > 0) ? ", " + cmd.Data[1].Value.ToString() : "") + ");";
                        sb.AppendLine(scriptCode);
                    }
                    break;
                case OldInteractionCommand.CommandType.AddScoreOnFirstExecution: // Add score on first execution
                    {
                        int points = cmd.Data[0].Value;
                        string newGuid = Guid.NewGuid().ToString();
                        string scriptCode = string.Format("if (Game.DoOnceOnly(\"{0}\"))", newGuid);
                        scriptCode += " {\n  ";
                        scriptCode += string.Format("GiveScore({0});", points);
                        scriptCode += "\n}";
                        sb.AppendLine(scriptCode);
                    }
                    break;
                default:
                    throw new AGS.Types.InvalidDataException("Invalid interaction type found");
            }
        }

        void ConvertInteractionCommandList(StringBuilder sb, OldInteractionCommandList cmdList, String scriptFuncPrefix, Game game, ref int runScriptCount, int targetTypeForUnhandledEvent)
        {
            bool onlyIfInvWasUsed = true;
            for (int cmd = 0; cmd < cmdList.CommandCount; cmd++)
            {
                ConvertInteractionToScript(sb, cmdList.Commands[cmd], scriptFuncPrefix, game, ref runScriptCount, ref onlyIfInvWasUsed, cmd);
                if (cmdList.Commands[cmd].Children != null)
                {
                    ConvertInteractionCommandList(sb, cmdList.Commands[cmd].Children, scriptFuncPrefix, game, ref runScriptCount, targetTypeForUnhandledEvent);
                    sb.AppendLine("}");
                }
            }
            if ((onlyIfInvWasUsed) && (targetTypeForUnhandledEvent > 0) && (cmdList.CommandCount > 0))
            {
                sb.AppendLine("else {");
                sb.AppendLine(string.Format("  unhandled_event({0}, 3);", targetTypeForUnhandledEvent));
                sb.AppendLine("}");
            }
        }

        void ConvertInteractions(Interactions interactions, OldInteraction oldInteraction, String scriptFuncPrefix, Game game, int targetTypeForUnhandledEvent)
        {
            if (oldInteraction.EventCount > interactions.ScriptFunctionNames.Length)
            {
                throw new AGSEditorException("Invalid interaction data: too many interaction events");
            }
            for (int i = 0; i < oldInteraction.EventCount; i++)
            {
                if (oldInteraction.Response[i] != null)
                {
                    int runScriptCount = 0;
                    StringBuilder sb = new StringBuilder();
                    ConvertInteractionCommandList(sb, oldInteraction.Response[i], scriptFuncPrefix, game, ref runScriptCount, targetTypeForUnhandledEvent);
                    if (runScriptCount == 1)
                    {
                        sb.Append("$$SINGLE_RUN_SCRIPT$$");
                    }
                    interactions.ImportedScripts[i] = sb.ToString();
                }
            }
        }

        #endregion // OldInteraction crap

        OldInteractionCommandList DeserializeOldInteractionCommandList(BinaryReader reader)
        {
            OldInteractionCommandList lst = new OldInteractionCommandList();
            lst.CommandCount = reader.ReadInt32();
            lst.TimesRun = reader.ReadInt32();
            lst.ReadCommandsFromFile(reader);
            for (int i = 0; i < lst.CommandCount; ++i)
            {
                if (lst.Commands[i].Children != null) lst.Commands[i].Children = DeserializeOldInteractionCommandList(reader);
                lst.Commands[i].Parent = lst;
            }
            return lst;
        }

        OldInteraction DeserializeOldInteraction(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1) return null;
            OldInteraction temp = new OldInteraction();
            temp.EventCount = reader.ReadInt32();
            if (temp.EventCount > OldInteraction.MAX_EVENTS)
            {
                throw new AGS.Types.InvalidDataException("Error: this interaction was saved with a newer version of AGS");
            }
            for (int i = 0; i < temp.EventCount; ++i)
            {
                temp.EventTypes[i] = reader.ReadInt32();
            }
            for (int i = 0; i < temp.EventCount; ++i)
            {
                temp.Response[i] = (reader.ReadInt32() != 0) ? new OldInteractionCommandList() : null;
            }
            for (int i = 0; i < temp.EventCount; ++i)
            {
                if (temp.Response[i] != null)
                {
                    temp.Response[i] = DeserializeOldInteractionCommandList(reader);
                }
                temp.TimesRun[i] = 0;
            }
            return temp;
        }

        private void DeserializeInteractionScripts(BinaryReader reader, Interactions interactions)
        {
            // TODO: I've botched some old macros into individual functions like this.
            //       They should really be implemented as class-wide constants. -monkey
            const int MAX_EVENTS = 30;
            int eventCount = reader.ReadInt32();
            if (eventCount > MAX_EVENTS) throw new AGS.Types.InvalidDataException("Too many interaction script events");
            for (int i = 0; i < eventCount; ++i)
            {
                StringBuilder sb = new StringBuilder(200);
                for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
                {
                    if (sb.Length < 200) sb = sb.Append(b > 0 ? b : 0);
                }
                interactions.ScriptFunctionNames[i] = sb.ToString();
            }
        }

        // TODO: get rid of this class
        private class MessageInfo
        {
            public enum MessageFlags
            {
                DisplayNext = 1, // supercedes using Alt-200 at end of message
                TimeLimit = 2
            };

            public enum DisplayAs
            {
                NormalWindow = 0,
                Speech = 1
            };

            public DisplayAs Display
            {
                get;
                set;
            }

            public MessageFlags Flags
            {
                get;
                set;
            }

            public void ReadFromFile(BinaryReader reader)
            {
                Display = (DisplayAs)reader.ReadByte();
                Flags = (MessageFlags)reader.ReadByte();
            }
        }

        string DecryptText(string source)
        {
            const string password = "Avis Durgan";
            StringBuilder sb = new StringBuilder(source.Length);
            for (int i = 0; i < source.Length; ++i)
            {
                sb.Append(source[i] - password[i % password.Length]);
            }
            return sb.ToString();
        }

        string ReadStringAndDecrypt(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if ((length < 0) || (length > 5000000)) throw new AGS.Types.InvalidDataException("Error reading string: file is corrupt");
            byte[] bytes = reader.ReadBytes(length);
            return DecryptText(Encoding.ASCII.GetString(bytes));
        }

        string FileGetStringLimit(BinaryReader reader, int limit)
        {
            StringBuilder sb = new StringBuilder(limit);
            int i = 0;
            for (i = 0; i < (limit - 1); ++i)
            {
                sb.Append(reader.ReadByte());
                if (sb[i] == 0) break;
            }
            while (sb[i] != 0) sb[i] = (char)reader.ReadByte();
            if (i == 0) return "";
            return sb.ToString(0, i);
        }

        [StructLayout(LayoutKind.Sequential, Size = 24)]
        struct DummyAnimationStruct
        {
            int X;
            int Y;
            int Data;
            int Object;
            int Speed;
            byte Action; // in native this is a one-byte char
            byte Wait; // char
            byte UnusedPadding0;
            byte UnusedPadding1;
        }

        [StructLayout(LayoutKind.Sequential, Size = 264)]
        struct DummyFullAnimation
        {
            // const int MAXANIMSTAGES = 10;
            DummyAnimationStruct Stage0;
            DummyAnimationStruct Stage1;
            DummyAnimationStruct Stage2;
            DummyAnimationStruct Stage3;
            DummyAnimationStruct Stage4;
            DummyAnimationStruct Stage5;
            DummyAnimationStruct Stage6;
            DummyAnimationStruct Stage7;
            DummyAnimationStruct Stage8;
            DummyAnimationStruct Stage9;
            int StageCount;
            int UnusedPadding0;
            int UnusedPadding1;
            int UnusedPadding2;
            int UnusedPadding3;
            int UnusedPadding4;
        }

        // TODO: I have no idea what I'm doing. -monkey
        void MyPutC(byte c, int maxSize, ref int putBytes, ref int outBytes, List<byte> memoryBuffer)
        {
            if (maxSize > 0)
            {
                putBytes++;
                if (putBytes > maxSize) return;
            }
            outBytes++;
            memoryBuffer.Add(c);
        }

        void LZWExpand(BinaryReader reader, int maxSize, ref int putBytes, ref int outBytes, List<byte> memoryBuffer)
        {
            int bits;
            byte ch;
            int i;
            int j;
            int len;
            int mask;
            List<byte> lzbuffer;
            putBytes = 0;
            const int N = 4096;
            const int F = 16;
            lzbuffer = new List<byte>(N);
            for (i = 0; i < N; ++i) lzbuffer.Add(0);
            i = N - F;
            while ((bits = reader.ReadByte()) != -1)
            {
                for (mask = 0x01; (mask & 0xFF) != 0; mask <<= 1)
                {
                    if ((bits & mask) != 0)
                    {
                        j = reader.ReadInt16();
                        len = ((j >> 12) & 15) + 3;
                        j = (i - j - 1) & (N - 1);
                        while (len-- > 0)
                        {
                            lzbuffer[i] = lzbuffer[j];
                            MyPutC(lzbuffer[i], maxSize, ref putBytes, ref outBytes, memoryBuffer);
                            j = (j + 1) & (N - 1);
                            i = (i + 1) & (N - 1);
                        }
                    }
                    else
                    {
                        ch = reader.ReadByte();
                        lzbuffer[i] = ch;
                        MyPutC(lzbuffer[i], maxSize, ref putBytes, ref outBytes, memoryBuffer);
                        i = (i + 1) & (N - 1);
                    }
                    if ((putBytes >= maxSize) && (maxSize > 0)) break;
                }
                if ((putBytes >= maxSize) && (maxSize > 0)) break;
            }
        }

        List<byte> LZWExpandToMemory(BinaryReader reader, int maxSize, ref int putBytes, ref int outBytes)
        {
            List<byte> memoryBuffer = new List<byte>(maxSize + 10);
            LZWExpand(reader, maxSize, ref putBytes, ref outBytes, memoryBuffer);
            return memoryBuffer;
        }

        int LoadLZW(BinaryReader reader, out Bitmap bmp, ref List<Color> palette)
        {
            const int paletteSize = 256;
            if (palette == null) palette = new List<Color>(paletteSize);
            else palette.Capacity = paletteSize;
            for (int i = 0; i < paletteSize; ++i)
            {
                byte[] rgb = reader.ReadBytes(4); // byte 4 is 'filler' (not alignment padding, but not apparently used)
                palette[i] = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
            }
            int maxSize = reader.ReadInt32();
            long uncompSize = reader.ReadInt32() + reader.BaseStream.Position;
            int outBytes = 0; // TODO: ref params on these mayhaps (depends how they're used elsewhere in relevant code)
            int putBytes = 0; //       in native these are global vars
            List<byte> memoryBuffer = LZWExpandToMemory(reader, maxSize, ref putBytes, ref outBytes);
            #region AGS_BIG_ENDIAN fixes (TODO: implement?)
            /*
               #if defined(AGS_BIG_ENDIAN)
                 AGS::Common::BBOp::SwapBytesInt32(loptr[0]);
                 AGS::Common::BBOp::SwapBytesInt32(loptr[1]);
                 int bitmapNumPixels = loptr[0]*loptr[1]/_acroom_bpp;
                 switch (_acroom_bpp) // bytes per pixel!
                 {
                   case 1:
                   {
                     // all done
                     break;
                   }
                   case 2:
                   {
                     short *sp = (short *)membuffer;
                     for (int i = 0; i < bitmapNumPixels; ++i)
                     {
                       AGS::Common::BBOp::SwapBytesInt16(sp[i]);
                     }
                     // all done
                     break;
                   }
                   case 4:
                   {
                     int *ip = (int *)membuffer;
                     for (int i = 0; i < bitmapNumPixels; ++i)
                     {
                       AGS::Common::BBOp::SwapBytesInt32(ip[i]);
                     }
                     // all done
                     break;
                   }
                 }
               #endif // defined(AGS_BIG_ENDIAN)
             */
            #endregion // AGS_BIG_ENDIAN fixes
            PixelFormat format = PixelFormat.Format16bppRgb565;
            switch (_bpp) // BYTES per pixel
            {
                // TODO: verify these formats as appropriate
                case 1: // 8-bit
                    format = PixelFormat.Format8bppIndexed;
                    break;
                case 2: // 16-bit
                    format = PixelFormat.Format16bppRgb565;
                    break;
                case 4: // 32-bit
                    format = PixelFormat.Format32bppRgb;
                    break;
                default:
                    break;
            }
            bmp = new Bitmap(memoryBuffer[0] / _bpp, memoryBuffer[1], format);
            for (int arin = 0; arin < memoryBuffer[1]; ++arin)
            {
                // TODO: I *definitely* don't know what I'm doing. -monkey
                BitmapData line = bmp.LockBits(new Rectangle(0, arin, bmp.Width, 1), ImageLockMode.ReadWrite, format);
                Marshal.Copy(memoryBuffer.ToArray(), (arin * memoryBuffer[0]) + 8, line.Scan0, memoryBuffer[0]);
                bmp.UnlockBits(line);
            }
            if (reader.BaseStream.Position != uncompSize) reader.BaseStream.Seek(uncompSize, SeekOrigin.Begin);
            return (int)uncompSize;
        }

        int CUnpackBitL(List<byte> line, int size, BinaryReader reader)
        {
            int n = 0; // number of bytes decoded
            line.Clear();
            line.Capacity = size;
            while (n < size)
            {
                int ix = reader.ReadByte(); // get index byte
                sbyte bx = (sbyte)ix;
                if (bx == -128) bx = 0;
                int i = 1 - bx; // ...run (if (bx < 0))
                if (bx >= 0) i = bx + 1; // ...seq
                while ((i--) != 0)
                {
                    // test for buffer overflow
                    if (n >= size) return -1;
                    line.Add(reader.ReadByte());
                    n++;
                }
            }
            return 0;
        }

        int LoadCompressedAllegro(BinaryReader reader, out Bitmap bmp, ref List<Color> palette)
        {
            short width = reader.ReadInt16();
            short height = reader.ReadInt16();
            bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            for (int i = 0; i < height; ++i)
            {
                List<byte> line = new List<byte>();
                CUnpackBitL(line, width, reader);
                // TODO: I *definitely* don't know what I'm doing. -monkey
                BitmapData bmpLine = bmp.LockBits(new Rectangle(0, i, bmp.Width, 1), ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);
                Marshal.Copy(line.ToArray(), 0, bmpLine.Scan0, width);
                bmp.UnlockBits(bmpLine);
            }
            reader.ReadBytes(768); // skip palette
            return (int)reader.BaseStream.Position;
        }

        RoomLoadError ReadMainBlock(BinaryReader reader)
        {
            if (_loadedVersion >= RoomFileVersion.Version208) _bpp = reader.ReadInt32();
            else _bpp = 1;
            if (_bpp < 1) _bpp = 1;
            _walkBehindCount = reader.ReadInt16();
            _walkBehinds.Clear();
            _walkBehinds.Capacity = _walkBehindCount;
            for (int i = 0; i < _walkBehindCount; ++i)
            {
                _walkBehinds.Add(new RoomWalkBehind());
                _walkBehinds[i].Baseline = reader.ReadInt16();
            }
            _hotspotCount = reader.ReadInt32();
            // FIXME check version? -per existing note from native code-
            if (_hotspotCount == 0) _hotspotCount = 20;
            _hotspots.Clear();
            _hotspots.Capacity = _hotspotCount;
            for (int i = 0; i < _hotspotCount; ++i)
            {
                _hotspots.Add(new RoomHotspot(null));
                _hotspots[i].WalkToPoint = new Point(reader.ReadInt16(), reader.ReadInt16());
            }
            int hotspotDescSize = 30;
            if (_loadedVersion >= RoomFileVersion.Version303A) hotspotDescSize = 2999;
            foreach (RoomHotspot hotspot in _hotspots)
            {
                byte[] bytes = new byte[hotspotDescSize];
                reader.Read(bytes, 0, hotspotDescSize);
                hotspot.Description = Encoding.ASCII.GetString(bytes);
            }
            if (_loadedVersion >= RoomFileVersion.Version270)
            {
                foreach (RoomHotspot hotspot in _hotspots)
                {
                    const int MAX_SCRIPT_NAME_LEN = 20; // TODO: remove limit on script names?
                    byte[] bytes = new byte[MAX_SCRIPT_NAME_LEN];
                    reader.Read(bytes, 0, MAX_SCRIPT_NAME_LEN);
                    hotspot.Name = Encoding.ASCII.GetString(bytes);
                }
            }
            _walkableAreaCount = reader.ReadInt32();
            _walkableAreas.Clear();
            _walkableAreas.Capacity = _walkableAreaCount;
            for (int i = 0; i < _walkableAreaCount; ++i)
            {
                _walkableAreas.Add(new RoomWalkableArea());
                if (_loadedVersion < RoomFileVersion.Version340Alpha)
                {
                    // read legacy WallPoints (never used, only written and read from room file)
                    // TODO: phase out, remove this from saved file data
                    const int MAXPOINTS = 30;
                    // read integers as bytes:
                    //   MAXPOINT x values
                    //   MAXPOINT y values
                    //   1 numpoint value
                    reader.ReadBytes(sizeof(int) * ((MAXPOINTS * 2) + 1));
                }
            }
            _edgeTop = reader.ReadInt16();
            _edgeBottom = reader.ReadInt16();
            _edgeLeft = reader.ReadInt16();
            _edgeRight = reader.ReadInt16();
            _objectCount = reader.ReadInt16();
            _objects.Clear();
            _objects.Capacity = _objectCount;
            for (int i = 0; i < _objectCount; ++i)
            {
                _objects.Add(new RoomObject(null));
                _objects[i].Image = reader.ReadInt16();
                _objects[i].StartX = reader.ReadInt16();
                _objects[i].StartY = reader.ReadInt16();
                _objects[i].ID = reader.ReadInt16();
                _objects[i].Visible = (reader.ReadInt16() != 0 ? true : false);
            }
            if (_loadedVersion >= RoomFileVersion.Version253) // TODO: phase out old interaction variables completely for newer versions
            {
                _localVariableCount = reader.ReadInt32();
                _localVariables.Clear();
                _localVariables.Capacity = _localVariableCount;
                for (int i = 0; i < _localVariableCount; ++i)
                {
                    _localVariables.Add(new OldInteractionVariable("IntVar_" + i.ToString(), 0));
                    byte[] bytes = new byte[23];
                    reader.Read(bytes, 0, 23);
                    _localVariables[i].Name = Encoding.ASCII.GetString(bytes);
                    if (_loadedVersion < RoomFileVersion.Version340Alpha)
                    {
                        // old 'type' variable is unused
                        // TODO: phase out during writing process
                        reader.ReadByte();
                    }
                    _localVariables[i].Value = reader.ReadInt32();
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version241)
            {
                if (_loadedVersion < RoomFileVersion.Version300A)
                {
                    for (int i = 0; i < _hotspotCount; ++i)
                    {
                        ConvertInteractions(_hotspots[i].Interactions, DeserializeOldInteraction(reader), "hotspot" + i.ToString() + "_", null, 1);
                    }
                    for (int i = 0; i < _objectCount; ++i)
                    {
                        ConvertInteractions(_objects[i].Interactions, DeserializeOldInteraction(reader), "object" + i.ToString() + "_", null, 2);
                    }
                    ConvertInteractions(_interactions, DeserializeOldInteraction(reader), "room_", null, 0);
                }
                if (_loadedVersion >= RoomFileVersion.Version255B)
                {
                    _regionCount = reader.ReadInt32();
                    _regions.Capacity = _regionCount;
                    for (int i = 0; i < _regionCount; ++i)
                    {
                        _regions.Add(new RoomRegion());
                        if (_loadedVersion < RoomFileVersion.Version300A)
                        {
                            ConvertInteractions(_regions[i].Interactions, DeserializeOldInteraction(reader), "region" + i.ToString() + "_", null, 0);
                        }
                    }
                }
                if (_loadedVersion >= RoomFileVersion.Version300A)
                {
                    DeserializeInteractionScripts(reader, _interactions);
                    for (int i = 0; i < _hotspotCount; ++i)
                    {
                        DeserializeInteractionScripts(reader, _hotspots[i].Interactions);
                    }
                    for (int i = 0; i < _objectCount; ++i)
                    {
                        DeserializeInteractionScripts(reader, _objects[i].Interactions);
                    }
                    for (int i = 0; i < _regionCount; ++i)
                    {
                        DeserializeInteractionScripts(reader, _regions[i].Interactions);
                    }
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version200Alpha)
            {
                for (int i = 0; i < _objectCount; ++i)
                {
                    _objects[i].Baseline = reader.ReadInt32();
                }
                _width = reader.ReadInt16();
                _height = reader.ReadInt16();
            }
            if (_loadedVersion >= RoomFileVersion.Version262)
            {
                for (int i = 0; i < _objectCount; ++i)
                {
                    int flags = reader.ReadInt16(); // TODO: this needs to be stored in the object somewhere!!
                    // _objects[i].Flags = flags; <-- something like that
                    /*
#define OBJF_NOINTERACT        1  // not clickable
#define OBJF_NOWALKBEHINDS     2  // ignore walk-behinds
#define OBJF_HASTINT           4  // the tint_* members are valid
#define OBJF_USEREGIONTINTS    8  // obey region tints/light areas
#define OBJF_USEROOMSCALING 0x10  // obey room scaling areas
#define OBJF_SOLID          0x20  // blocks characters from moving
#define OBJF_DELETED        0x40  // object has been deleted
                     */
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version200Final)
            {
                _resolution = (RoomResolution)reader.ReadInt16();
            }
            if (_loadedVersion >= RoomFileVersion.Version240)
            {
                _walkableAreaCount = reader.ReadInt32();
            }
            else
            {
                const int LEGACY_MAX_WALKABLE_AREAS = 15;
                _walkableAreaCount = LEGACY_MAX_WALKABLE_AREAS;
            }
            _walkableAreas.Capacity = _walkBehindCount;
            for (int i = 0; i < _walkBehindCount; ++i)
            {
                _walkableAreas.Add(new RoomWalkableArea());
            }
            if (_loadedVersion >= RoomFileVersion.Version200Alpha7)
            {
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    _walkableAreas[i].MinScalingLevel = reader.ReadInt16() + 100;
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version214)
            {
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    int light = reader.ReadInt16(); // TODO: store this in the area
                    // _walkableAreas[i].LightLevel = light; <-- something like that
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version251)
            {
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    // TODO: CHECKME, the engine uses these properties weirdly
                    _walkableAreas[i].MaxScalingLevel = reader.ReadInt16() + 100;
                    if (_walkableAreas[i].MinScalingLevel == _walkableAreas[i].MaxScalingLevel)
                    {
                        _walkableAreas[i].UseContinuousScaling = true;
                        _walkableAreas[i].MaxScalingLevel = 0;
                        _walkableAreas[i].ScalingLevel = _walkableAreas[i].MinScalingLevel;
                    }
                    else _walkableAreas[i].UseContinuousScaling = false;
                }
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    int top = reader.ReadInt16(); // TODO: store in area
                    // _walkableAreas[i].Top = top; <-- something like that
                }
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    int bottom = reader.ReadInt16(); // TODO: store in area
                    // _walkableAreas[i].Bottom = bottom; <-- something like that
                }
                if (_loadedVersion < RoomFileVersion.Version340Alpha)
                {
                    reader.ReadBytes(11); // read the room password (not used)
                }
                else _stateSaving = reader.ReadBoolean();
            }
            byte[] options = reader.ReadBytes(_options.Length);
            for (int i = 0; i < _options.Length; ++i)
            {
                _options[i] = (int)options[i];
            }
            _messageCount = reader.ReadInt16();
            _messages.Capacity = _messageCount;
            if (_loadedVersion >= RoomFileVersion.Version272)
            {
                _gameID = reader.ReadInt32();
            }
            if (_loadedVersion >= RoomFileVersion.Pre114Version3)
            {
                _messageInfos.Capacity = _messageCount;
                for (int i = 0; i < _messageCount; ++i)
                {
                    _messageInfos[i].ReadFromFile(reader);
                }
            }
            string messageBuffer = null;
            for (int i = 0; i < _messageCount; ++i)
            {
                if (_loadedVersion >= RoomFileVersion.Version261)
                {
                    messageBuffer = ReadStringAndDecrypt(reader);
                }
                else messageBuffer = FileGetStringLimit(reader, 2999);
                if ((messageBuffer.Length > 0) && (messageBuffer[messageBuffer.Length - 1] == (char)200))
                {
                    messageBuffer = messageBuffer.Substring(0, messageBuffer.Length - 1);
                    _messageInfos[i].Flags |= MessageInfo.MessageFlags.DisplayNext;
                    _messages[i] = messageBuffer;
                }
            }
            if (_loadedVersion >= RoomFileVersion.Pre114Version6)
            {
                int animationCount = reader.ReadInt16();
                if (animationCount > 0)
                {
                    // [IKM] (from native) CHECKME later: this will cause trouble if structure changes
                    // TODO: phase this out of the writing process! It's obviously not being read into
                    //       the structure. -monkey
                    // TODO: Verify this as getting correct size for current structure layout.
                    int size = Marshal.SizeOf(new DummyFullAnimation());
                    reader.ReadBytes(size * animationCount);
                }
            }
            if ((_loadedVersion >= RoomFileVersion.Pre114Version4) && (_loadedVersion < RoomFileVersion.Version250A))
            {
                if (reader.ReadInt32() != 1) throw new AGS.Types.InvalidDataException("ScriptEdit: invalid config version");
                int numVarNames = reader.ReadInt32();
                for (int i = 0; i < numVarNames; ++i)
                {
                    int lenoft = reader.ReadByte(); // who the what now?
                    reader.ReadBytes(lenoft);
                }
                int ct = 0;
                while (true)
                {
                    ct = reader.ReadInt32();
                    if (ct == -1) break;
                    int lee = reader.ReadInt32();
                    reader.ReadBytes(lee);
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version114)
            {
                for (int i = 0; i < _walkableAreaCount; ++i)
                {
                    int shadingView = reader.ReadInt16(); // TODO: store this in the structure if it's being used
                    // _walkableAreas[i].ShadingView = shadingView; <-- like that
                }
                const int LEGACY_MAX_ROOM_WALKAREAS = 15;
                for (int i = _walkableAreaCount; i < (LEGACY_MAX_ROOM_WALKAREAS + 1); ++i)
                {
                    reader.ReadInt16();
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version255B)
            {
                for (int i = 0; i < _regionCount; ++i)
                {
                    _regions[i].LightLevel = reader.ReadInt16();
                }
                for (int i = 0; i < _regionCount; ++i)
                {
                    int tint = reader.ReadInt32(); // TODO: split between Region.RedTint, GreenTint, and BlueTint
                }
            }
            if (_loadedVersion >= RoomFileVersion.Pre114Version5)
            {
                Bitmap bmp;
                LoadLZW(reader, out bmp, ref _palette);
                _backgrounds[0].Graphic = bmp;
            }
            else
            {
                Bitmap bmp;
                LoadCompressedAllegro(reader, out bmp, ref _palette);
                _backgrounds[0].Graphic = bmp;
            }
            if ((_backgrounds[0].Graphic.Width > 320) && (_loadedVersion < RoomFileVersion.Version200Final))
            {
                _resolution = RoomResolution.HighRes;
            }
            if (_loadedVersion >= RoomFileVersion.Version255B)
            {
                LoadCompressedAllegro(reader, out _regionMask, ref _palette);
            }
            else if (_loadedVersion >= RoomFileVersion.Version114)
            {
                Bitmap bmp;
                LoadCompressedAllegro(reader, out bmp, ref _palette);
                // an old version, this mask is just deleted anyway
            }
            LoadCompressedAllegro(reader, out _walkableAreaMask, ref _palette);
            LoadCompressedAllegro(reader, out _walkBehindMask, ref _palette);
            LoadCompressedAllegro(reader, out _hotspotMask, ref _palette);
            if (_loadedVersion < RoomFileVersion.Version255B)
            {
                // old version - copy walkable areas to Regions
                if (_regionMask == null)
                {
                    _regionMask = new Bitmap(_walkableAreaMask);
                    _regionCount = _walkableAreaCount;
                    _regions.Capacity = _regionCount;
                    for (int i = 0; i < _regionCount; ++i)
                    {
                        _regions.Add(new RoomRegion());
                        // TODO: copy light level? RoomWalkableArea doesn't have a LightLevel property
                    }
                }
            }
            return RoomLoadError.NoError;
        }

        RoomLoadError ReadBlock(BinaryReader reader, RoomFormatBlock blockType)
        {
            if ((reader == null) || (blockType == RoomFormatBlock.End)) return RoomLoadError.InternalLogicError;
            int blockLength = reader.ReadInt32();
            long nextBlockPos = reader.BaseStream.Position + blockLength;
            RoomLoadError readBlockError = RoomLoadError.NoError;
            switch (blockType)
            {
                case RoomFormatBlock.Main:
                    readBlockError = ReadMainBlock(reader);
                    break;
                case RoomFormatBlock.Script:
                    readBlockError = RoomLoadError.NoError;
                    break;
                case RoomFormatBlock.CompiledScript:
                case RoomFormatBlock.CompiledScript2:
                    return RoomLoadError.OldBlockNotSupported;
                case RoomFormatBlock.ObjectNames:
                    readBlockError = RoomLoadError.NoError;
                    break;
                case RoomFormatBlock.AnimatedBackground:
                    readBlockError = RoomLoadError.NoError;
                    break;
                case RoomFormatBlock.CompiledScript3:
                    readBlockError = RoomLoadError.NoError;
                    break;
                case RoomFormatBlock.Properties:
                    readBlockError = RoomLoadError.NoError;
                    break;
                case RoomFormatBlock.ObjectScriptNames:
                    readBlockError = RoomLoadError.NoError;
                    break;
                default:
                    return RoomLoadError.UnknownBlockType;
            }
            if (readBlockError != RoomLoadError.NoError) return readBlockError;
            long currentPos = reader.BaseStream.Position;
            if (currentPos != nextBlockPos)
            {
                // TODO: raise warning, blocks nonsequential
                // native just prints warning to output log
                reader.BaseStream.Seek(nextBlockPos, SeekOrigin.Begin);
            }
            return RoomLoadError.NoError;
        }

        /*RoomInfoError RoomInfo::ReadBlock(Stream *in, RoomFormatBlock block_type)
{
    switch (block_type)
    {
    case kRoomBlock_Main:
        read_block_err = ReadMainBlock(in);
        break;
    case kRoomBlock_Script:
        read_block_err = ReadScriptBlock(in);
        break;
    case kRoomBlock_ObjectNames:
        read_block_err = ReadObjectNamesBlock(in);
        break;
    case kRoomBlock_AnimBkg:
        read_block_err = ReadAnimBkgBlock(in);
        break;
    case kRoomBlock_CompScript3:
        read_block_err = ReadScript3Block(in);
        break;
    case kRoomBlock_Properties:
        read_block_err = ReadPropertiesBlock(in);
        break;
    case kRoomBlock_ObjectScriptNames:
        read_block_err = ReadObjectScriptNamesBlock(in);
        break;
    };
}*/

        private RoomLoadError ReadFromFile(BinaryReader reader, int roomNumber, bool hiRes, ref RoomFormatBlock lastBlock)
        {
            Free();
            if (reader == null) return RoomLoadError.InternalLogicError;
            InitializeDefaults();
            _loadedVersion = (RoomFileVersion)reader.ReadInt16();
            if ((_loadedVersion < RoomFileVersion.Version250B) || (_loadedVersion > RoomFileVersion.Current))
            {
                return RoomLoadError.FormatNotSupported;
            }
            RoomFormatBlock blockType = RoomFormatBlock.None;
            while (blockType != RoomFormatBlock.End)
            {
                blockType = (RoomFormatBlock)reader.ReadByte();
                lastBlock = blockType;
                if (blockType < 0) return RoomLoadError.UnexpectedEOF;
                RoomLoadError error = ReadBlock(reader, blockType);
                if (error != RoomLoadError.NoError)
                {
                    return error;
                }
            }
            // TODO: ProcessAfterRead(id, game_is_hires); -- FROM NATIVE
            return RoomLoadError.NoError;
        }

        public static Room Load(int roomNumber)
        {
            return null;
        }
        /*update_polled_stuff_if_runtime();

    Stream *in = AssetManager::OpenAsset(filename);
    if (in == NULL)
    {
        String err_msg = String::FromFormat(
            "Load_room: Unable to load the room file '%s'\n"
            "Make sure that you saved the room to the correct folder (it should be\n"
            "in your game's sub-folder of the AGS directory).\n"
            "Also check that the player character's starting room is set correctly.\n",
            filename.GetCStr());
        quit(err_msg);
    }
    update_polled_stuff_if_runtime();  // it can take a while to load the file sometimes

    RoomFormatBlock last_block;
    RoomInfoError load_err = room.ReadFromFile(in, id, game_is_hires, &last_block);
    delete in;

    switch (load_err)
    {
    case kRoomInfoErr_InternalLogicError:
        quit("Load_Room: internal logic error.\n");
        break;
    case kRoomInfoErr_UnexpectedEOF:
        quit("LoadRoom: unexpected end of file while loading room");
        break;
    case kRoomInfoErr_FormatNotSupported:
        quit("Load_Room: Bad packed file. Either the file requires a newer or older version of\n"
            "this program or the file is corrupt.\n");
        break;
    case kRoomInfoErr_UnknownBlockType:
        quit(String::FromFormat("LoadRoom: unknown block type %d encountered in '%s'", last_block, filename.GetCStr()));
        break;
    case kRoomInfoErr_OldBlockNotSupported:
        quit("Load_room: old room format. Please upgrade the room.");
        break;
    case kRoomInfoErr_InconsistentDataForObjectNames:
        quit("Load_room: inconsistent blocks for object names");
        break;
    case kRoomInfoErr_ScriptLoadFailed:
        quit("Load_room: Script load failed; need newer version?");
        break;
    case kRoomInfoErr_PropertiesFormatNotSupported:
        quit("LoadRoom: unknown Custom Properties block encounreted");
        break;
    case kRoomInfoErr_PropertiesLoadFailed:
        quit("LoadRoom: error reading custom properties block");
        break;
    case kRoomInfoErr_InconsistentDataForObjectScriptNames:
        quit("Load_room: inconsistent blocks for object script names");
        break;
    }

    return load_err == kRoomInfoErr_NoError;*/
    }
}
