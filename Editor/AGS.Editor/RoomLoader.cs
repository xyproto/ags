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
        // TODO: Make sure that this all does what it's supposed to. -monkey

        // TODO: Split the methods here into other classes and files!
        //       I'm keeping it localized for now to make sure everything fits together
        //       and hopefully works before I distribute the code appropriately.
        //       Porting from native has not been a joke, it's hard work! :) -monkey

        // TODO: probably add these bitmaps to the Room structure.
        private List<RoomBackground> _backgrounds = new List<RoomBackground>();
        private Bitmap _hotspotMask;
        private Bitmap _regionMask;
        private Bitmap _walkableAreaMask;
        private Bitmap _walkBehindMask;
        private RoomFileVersion _loadedVersion;
        // TODO: get rid of this array, we can just read the members into the structure.
        private int[] _options = new int[10];
        private List<Color> _palette = new List<Color>();
        private CompiledScript _compiledScript; // TODO: this is unused? (I can't find any references in native either)
        private string _textScript; // TODO: unused?
        private Room _loadingRoom;
        /// <summary>
        /// This is just temporary storage beause Walkable Areas don't have a light level.
        /// Some legacy files copy this member into regions though.
        /// </summary>
        private List<int> _legacyWalkableAreaLightLevel = new List<int>();

        /// <summary>
        /// Various constants used throughout this class.
        /// This separate class is for scoping purposes only.
        /// </summary>
        public static class RoomLoaderConstants
        {
            public const int MAX_SCRIPT_NAME_LEN = 20;
            public const int NOT_VECTOR_SCALED = -10000;
            public const string ROOM_PASSWORD = "Avis Durgan";
        }

        public class RoomBackground
        {
            public const int PALETTE_SIZE = 256;

            public Bitmap Graphic
            {
                get;
                set;
            }

            public List<Color> Palette
            {
                get;
                set;
            }

            public bool PaletteShared
            {
                get;
                set;
            }

            public int ID
            {
                get;
                set;
            }

            public RoomBackground()
            {
                Palette = new List<Color>(PALETTE_SIZE);
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
            InconsistentDataForObjectNames,
            ScriptLoadFailed,
            PropertiesFormatNotSupported,
            PropertiesLoadFailed,
            InconsistentDataForObjectScriptNames
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
        }

        private void InitializeDefaults()
        {
            _loadingRoom = null;
            _loadedVersion = RoomFileVersion.Current;
            _backgrounds.Clear();
            _backgrounds.Add(new RoomBackground());
            _backgrounds[0].ID = 0;
            _hotspotMask = null;
            _regionMask = null;
            _walkableAreaMask = null;
            _walkBehindMask = null;
            _palette.Clear();
            Array.Clear(_options, 0, _options.Length);
            _compiledScript = null;
            _textScript = null;
            _legacyWalkableAreaLightLevel.Clear();
        }

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

        private static class OldInteractionConstants
        {
            public const int MAX_ACTION_ARGS = 5;
            public const int MAX_EVENTS = 30;
        }

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

            public OldInteractionCommandList(OldInteractionCommandList other)
                : this()
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
                Data = new OldInteractionValue[OldInteractionConstants.MAX_ACTION_ARGS];
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
                EventTypes = new int[OldInteractionConstants.MAX_EVENTS];
                TimesRun = new int[OldInteractionConstants.MAX_EVENTS];
                Response = new OldInteractionCommandList[OldInteractionConstants.MAX_EVENTS];
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
                for (int i = 0; i < OldInteractionConstants.MAX_EVENTS; ++i)
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
                for (int i = 0; i < OldInteractionConstants.MAX_EVENTS; ++i)
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
                ArgumentTypes = new ActionArgument[OldInteractionConstants.MAX_ACTION_ARGS];
                ArgumentNames = new string[OldInteractionConstants.MAX_ACTION_ARGS];
            }

            public OldInteractionAction(string name, ActionFlags flags, int argCount, ActionArgument[] argTypes, string[] argNames, string description, string textScript)
                : this()
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
            if (temp.EventCount > OldInteractionConstants.MAX_EVENTS)
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
            int eventCount = reader.ReadInt32();
            if (eventCount > OldInteractionConstants.MAX_EVENTS) throw new AGS.Types.InvalidDataException("Too many interaction script events");
            for (int i = 0; i < eventCount; ++i)
            {
                StringBuilder sb = new StringBuilder(200);
                for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
                {
                    if (sb.Length < 200) sb = sb.Append(b);
                }
                interactions.ScriptFunctionNames[i] = sb.ToString();
            }
        }

        string DecryptText(string source)
        {
            const string password = RoomLoaderConstants.ROOM_PASSWORD;
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
            return DecryptText(GetNullTerminatedASCIIString(bytes));
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
            const int N = 4096;
            const int F = 16;
            byte[] lzbuffer = new byte[N];
            int i = (N - F);
            for (int bits = reader.ReadByte(); bits != -1; bits = reader.ReadByte())
            {
                for (int mask = 0x01; (mask & 0xFF) != 0; mask <<= 1)
                {
                    if ((bits & mask) != 0)
                    {
                        int j = reader.ReadInt16();
                        int len = (((j >> 12) & 15) + 3);
                        j = ((i - j - 1) & (N - 1));
                        for (; len != 0; --len)
                        {
                            lzbuffer[i] = lzbuffer[j];
                            MyPutC(lzbuffer[i], maxSize, ref putBytes, ref outBytes, memoryBuffer);
                            j = ((j + 1) & (N - 1));
                            i = ((i + 1) & (N - 1));
                        }
                    }
                    else
                    {
                        lzbuffer[i] = reader.ReadByte();
                        MyPutC(lzbuffer[i], maxSize, ref putBytes, ref outBytes, memoryBuffer);
                        i = ((i + 1) & (N - 1));
                    }
                    if ((maxSize > 0) && (putBytes >= maxSize)) break;
                }
                if ((maxSize > 0) && (putBytes >= maxSize)) break;
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
                palette.Add(Color.FromArgb(rgb[0], rgb[1], rgb[2]));
            }
            int maxSize = reader.ReadInt32();
            long uncompSize = reader.ReadInt32() + reader.BaseStream.Position;
            int outBytes = 0; // TODO: ref params on these mayhaps (depends how they're used elsewhere in relevant code)
            int putBytes = 0; //       in native these are global vars (otherwise possibly delete them)
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
            switch (_loadingRoom.ColorDepth) // bits per pixel
            {
                // TODO: verify these formats as appropriate
                case 8:
                    format = PixelFormat.Format8bppIndexed;
                    break;
                case 16:
                    format = PixelFormat.Format16bppRgb565;
                    break;
                case 32:
                    format = PixelFormat.Format32bppRgb;
                    break;
                default:
                    break;
            }
            // first 8 bytes of memory buffer are (width*bpp) and height as 32-bit ints
            int lineWidth = BitConverter.ToInt32(memoryBuffer.ToArray(), 0);
            int height = BitConverter.ToInt32(memoryBuffer.ToArray(), sizeof(int));
            bmp = new Bitmap(lineWidth / (_loadingRoom.ColorDepth / 8), height, format);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, format);
            Marshal.Copy(memoryBuffer.ToArray(), sizeof(int) * 2, bmpData.Scan0, lineWidth * height);
            bmp.UnlockBits(bmpData);
            if (reader.BaseStream.Position != uncompSize) reader.BaseStream.Seek(uncompSize, SeekOrigin.Begin);
            return (int)uncompSize;
        }

        int CUnpackBitL(List<byte> line, int size, BinaryReader reader)
        {
            line.Clear();
            line.Capacity = size;
            int n = 0; // number of bytes decoded
            while (n < size)
            {
                sbyte bx = reader.ReadSByte(); // get index byte
                if (bx == -128) bx = 0;
                int i = 1 + System.Math.Abs(bx);
                byte b = reader.ReadByte();
                for (; i != 0; --i)
                {
                    // test for buffer overflow
                    if (n >= size) return -1;
                    n++;
                    line.Add(b);
                    if (bx >= 0) b = reader.ReadByte(); // seq
                }
            }
            return 0;
        }

        int LoadCompressedAllegro(BinaryReader reader, out Bitmap bmp)
        {
            short width = reader.ReadInt16();
            short height = reader.ReadInt16();
            bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            List<byte> line = new List<byte>();
            for (int i = 0; i < height; ++i)
            {
                CUnpackBitL(line, width, reader); // line is cleared and capacity set by cunpack
                // CHECKME: if locking and unlocking is slow, the entire bitmap could be read into
                //          single list and then copied at once
                BitmapData bmpLine = bmp.LockBits(new Rectangle(0, i, bmp.Width, 1), ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);
                Marshal.Copy(line.ToArray(), 0, bmpLine.Scan0, width);
                bmp.UnlockBits(bmpLine);
            }
            reader.ReadBytes(768); // skip palette
            return (int)reader.BaseStream.Position;
        }

        /// <summary>
        /// Reads a null-terminated string from a file. You can limit the length of the
        /// returned string by specifying the 'limit' param. If 'stopAtLimit' is true
        /// then this function will not read more than limit bytes from the file;
        /// otherwise it will keep reading until it finds a null character, but it will
        /// stop appending to the string at the limit.
        /// </summary>
        string ReadNullTerminatedString(BinaryReader reader, int limit, bool stopAtLimit)
        {
            StringBuilder sb = new StringBuilder(limit);
            for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            {
                if (sb.Length < limit) sb.Append(b);
                else if (stopAtLimit) break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns an ASCII-encoded string from an array of bytes, and truncates the
        /// string to the index of the first null-terminator (if any).
        /// </summary>
        public static string GetNullTerminatedASCIIString(byte[] bytes)
        {
            string s = Encoding.ASCII.GetString(bytes);
            int idx = s.IndexOf((char)0);
            return s.Substring(0, (idx == -1 ? s.Length : idx));
        }

        RoomLoadError ReadMainBlock(BinaryReader reader)
        {
            int bytesPerPixel = 1;
            if (_loadedVersion >= RoomFileVersion.Version208) bytesPerPixel = reader.ReadInt32();
            if (bytesPerPixel < 1) bytesPerPixel = 1;
            _loadingRoom.ColorDepth = (bytesPerPixel * 8);
            int walkBehindCount = reader.ReadInt16();
            _loadingRoom.WalkBehinds.Clear();
            for (int i = 0; i < walkBehindCount; ++i)
            {
                _loadingRoom.WalkBehinds.Add(new RoomWalkBehind());
                _loadingRoom.WalkBehinds[i].Baseline = reader.ReadInt16();
                _loadingRoom.WalkBehinds[i].ID = i;
            }
            int hotspotCount = reader.ReadInt32();
            if (hotspotCount == 0) hotspotCount = 20; // this is NOT the same as legacy max
            _loadingRoom.Hotspots.Clear();
            for (int i = 0; i < hotspotCount; ++i)
            {
                _loadingRoom.Hotspots.Add(new RoomHotspot(_loadingRoom));
                _loadingRoom.Hotspots[i].WalkToPoint = new Point(reader.ReadInt16(), reader.ReadInt16());
                _loadingRoom.Hotspots[i].ID = i;
            }
            int hotspotDescSize = 30;
            bool stopAtLimit = true;
            if (_loadedVersion >= RoomFileVersion.Version303A)
            {
                hotspotDescSize = 2999;
                stopAtLimit = false;
            }
            foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
            {
                hotspot.Description = ReadNullTerminatedString(reader, hotspotDescSize, stopAtLimit);
            }
            if (_loadedVersion >= RoomFileVersion.Version270)
            {
                foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
                {
                    byte[] bytes = reader.ReadBytes(RoomLoaderConstants.MAX_SCRIPT_NAME_LEN);
                    hotspot.Name = GetNullTerminatedASCIIString(bytes);
                }
            }
            int legacyWallPointCount = reader.ReadInt32();
            for (int i = 0; i < legacyWallPointCount; ++i)
            {
                // read legacy WallPoints (never used, only written and read from room file)
                // TODO: phase out, remove this from saved file data
                const int MAX_POINTS = 30;
                // read integers as bytes:
                //   MAXPOINT x values
                //   MAXPOINT y values
                //   1 numpoint value
                reader.ReadBytes(sizeof(int) * ((MAX_POINTS * 2) + 1));
            }
            _loadingRoom.TopEdgeY = reader.ReadInt16();
            _loadingRoom.BottomEdgeY = reader.ReadInt16();
            _loadingRoom.LeftEdgeX = reader.ReadInt16();
            _loadingRoom.RightEdgeX = reader.ReadInt16();
            _loadingRoom.Objects.Clear();
            int objectCount = reader.ReadInt16();
            for (int i = 0; i < objectCount; ++i)
            {
                _loadingRoom.Objects.Add(new RoomObject(_loadingRoom));
                _loadingRoom.Objects[i].Image = reader.ReadInt16();
                _loadingRoom.Objects[i].StartX = reader.ReadInt16();
                _loadingRoom.Objects[i].StartY = reader.ReadInt16();
                int unusedRoomIndex = reader.ReadInt16(); // TODO: phase out, this isn't used anywhere
                _loadingRoom.Objects[i].Visible = (reader.ReadInt16() != 0);
                _loadingRoom.Objects[i].ID = i;
            }
            if (_loadedVersion >= RoomFileVersion.Version253) // TODO: phase out old interaction variables completely for newer versions
            {
                _loadingRoom.OldInteractionVariables.Clear();
                int oldInteractionVariableCount = reader.ReadInt32();
                for (int i = 0; i < oldInteractionVariableCount; ++i)
                {
                    byte[] bytes = reader.ReadBytes(23);
                    string name = GetNullTerminatedASCIIString(bytes);
                    reader.ReadByte(); // old 'type' variable is unused
                    int value = reader.ReadInt32();
                    _loadingRoom.OldInteractionVariables.Add(new OldInteractionVariable(name, value));
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version241)
            {
                if (_loadedVersion < RoomFileVersion.Version300A)
                {
                    foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
                    {
                        ConvertInteractions(hotspot.Interactions, DeserializeOldInteraction(reader), "hotspot" + hotspot.ID.ToString() + "_", AGSEditor.Instance.CurrentGame, 1);
                    }
                    foreach (RoomObject obj in _loadingRoom.Objects)
                    {
                        ConvertInteractions(obj.Interactions, DeserializeOldInteraction(reader), "object" + obj.ID.ToString() + "_", AGSEditor.Instance.CurrentGame, 2);
                    }
                    ConvertInteractions(_loadingRoom.Interactions, DeserializeOldInteraction(reader), "room_", AGSEditor.Instance.CurrentGame, 0);
                }
                if (_loadedVersion >= RoomFileVersion.Version255B)
                {
                    int regionCount = reader.ReadInt32();
                    _loadingRoom.Regions.Clear();
                    for (int i = 0; i < regionCount; ++i)
                    {
                        _loadingRoom.Regions.Add(new RoomRegion());
                        _loadingRoom.Regions[i].ID = i;
                        if (_loadedVersion < RoomFileVersion.Version300A)
                        {
                            ConvertInteractions(_loadingRoom.Regions[i].Interactions, DeserializeOldInteraction(reader), "region" + i.ToString() + "_", AGSEditor.Instance.CurrentGame, 0);
                        }
                    }
                }
                if (_loadedVersion >= RoomFileVersion.Version300A)
                {
                    DeserializeInteractionScripts(reader, _loadingRoom.Interactions);
                    foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
                    {
                        DeserializeInteractionScripts(reader, hotspot.Interactions);
                    }
                    foreach (RoomObject obj in _loadingRoom.Objects)
                    {
                        DeserializeInteractionScripts(reader, obj.Interactions);
                    }
                    foreach (RoomRegion region in _loadingRoom.Regions)
                    {
                        DeserializeInteractionScripts(reader, region.Interactions);
                    }
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version200Alpha)
            {
                foreach (RoomObject obj in _loadingRoom.Objects)
                {
                    obj.Baseline = reader.ReadInt32();
                }
                _loadingRoom.Width = reader.ReadInt16();
                _loadingRoom.Height = reader.ReadInt16();
            }
            if (_loadedVersion >= RoomFileVersion.Version262)
            {
                foreach (RoomObject obj in _loadingRoom.Objects)
                {
                    int flags = reader.ReadInt16();
                    obj.UseRoomAreaScaling = ((flags & 0x10) != 0);
                    obj.UseRoomAreaLighting = ((flags & 8) != 0);
                    // other object flags are used solely by the runtime engine
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version200Final)
            {
                _loadingRoom.Resolution = (RoomResolution)reader.ReadInt16();
            }
            _loadingRoom.WalkableAreas.Clear();
            int walkableAreaCount = 0;
            if (_loadedVersion >= RoomFileVersion.Version240)
            {
                walkableAreaCount = reader.ReadInt32();
            }
            else walkableAreaCount = Room.LEGACY_MAX_WALKABLE_AREAS;
            for (int i = 0; i < walkableAreaCount; ++i)
            {
                _loadingRoom.WalkableAreas.Add(new RoomWalkableArea());
                _loadingRoom.WalkableAreas[i].ID = i;
            }
            if (_loadedVersion >= RoomFileVersion.Version200Alpha7)
            {
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    area.MinScalingLevel = reader.ReadInt16();
                    // NOTE: This isn't the final value for this property, but it will be
                    // fixed later, in Room.Load. (This is not a TODO, just an annotation)
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version214)
            {
                _legacyWalkableAreaLightLevel.Clear();
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    int light = reader.ReadInt16(); // TODO: this isn't used by the editor, phase it out of writing
                    _legacyWalkableAreaLightLevel.Add(light); // however, it is used by legacy rooms as the region light level, so hang onto it for now
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version251)
            {
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    area.MaxScalingLevel = reader.ReadInt16();
                    if (area.MinScalingLevel == area.MaxScalingLevel)
                    {
                        area.MaxScalingLevel = RoomLoaderConstants.NOT_VECTOR_SCALED;
                    }
                    // NOTE: This isn't the final value for this property, but it will be
                    // fixed later, in Room.Load. (This is not a TODO, just an annotation)
                }
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    int top = reader.ReadInt16(); // TODO: this isn't used by editor, phase it out of writing
                }
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    int bottom = reader.ReadInt16(); // TODO: this isn't used by editor, phase it out of writing
                }
                if (_loadedVersion < RoomFileVersion.Version340Alpha)
                {
                    reader.ReadBytes(RoomLoaderConstants.ROOM_PASSWORD.Length); // read the room password (not used)
                }
                else _loadingRoom.StateSaving = reader.ReadBoolean();
            }
            byte[] options = reader.ReadBytes(_options.Length);
            for (int i = 0; i < _options.Length; ++i)
            {
                // TODO: read directly into _loadingRoom here instead of delaying it...
                _options[i] = (int)options[i];
            }
            _loadingRoom.Messages.Clear();
            int messageCount = reader.ReadInt16();
            for (int i = 0; i < messageCount; ++i)
            {
                _loadingRoom.Messages.Add(new RoomMessage(i));
            }
            if (_loadedVersion >= RoomFileVersion.Version272)
            {
                _loadingRoom.GameID = reader.ReadInt32();
            }
            if (_loadedVersion >= RoomFileVersion.Pre114Version3)
            {
                foreach (RoomMessage message in _loadingRoom.Messages)
                {
                    int display = reader.ReadByte();
                    int flags = reader.ReadByte();
                    message.ShowAsSpeech = (display > 0);
                    message.CharacterID = ((int)display - 1); // ...what? apparently this only returns -1 or 0
                    message.DisplayNextMessageAfter = ((flags & 1) != 0);
                    message.AutoRemoveAfterTime = ((flags & 2) != 0);
                }
            }
            string messageBuffer = null;
            foreach (RoomMessage message in _loadingRoom.Messages)
            {
                if (_loadedVersion >= RoomFileVersion.Version261)
                {
                    messageBuffer = ReadStringAndDecrypt(reader);
                }
                else messageBuffer = FileGetStringLimit(reader, 2999);
                if ((messageBuffer.Length > 0) && (messageBuffer[messageBuffer.Length - 1] == (char)200))
                {
                    messageBuffer = messageBuffer.Substring(0, messageBuffer.Length - 1);
                    message.DisplayNextMessageAfter = true;
                }
                message.Text = messageBuffer;
            }
            if (_loadedVersion >= RoomFileVersion.Pre114Version6)
            {
                int animationCount = reader.ReadInt16();
                if (animationCount > 0)
                {
                    // [IKM] (from native) CHECKME later: this will cause trouble if structure changes
                    // TODO: phase this out of the writing process! It's obviously not being read into
                    //       the structure. Once it's phased out then future changes to the structure
                    //       on the engine side won't matter. -monkey
                    // TODO: Verify this as getting correct size for current structure layout.
                    int size = Marshal.SizeOf(new DummyFullAnimation());
                    reader.ReadBytes(size * animationCount);
                }
            }
            if ((_loadedVersion >= RoomFileVersion.Pre114Version4) && (_loadedVersion < RoomFileVersion.Version250A))
            {
                // none of this is actually implemented
                if (reader.ReadInt32() != 1) throw new AGS.Types.InvalidDataException("ScriptEdit: invalid config version");
                int numVarNames = reader.ReadInt32();
                for (int i = 0; i < numVarNames; ++i)
                {
                    int lenoft = reader.ReadByte();
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
                foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
                {
                    area.AreaSpecificView = reader.ReadInt16();
                }
                for (int i = _loadingRoom.WalkableAreas.Count; i < Room.LEGACY_MAX_WALKABLE_AREAS; ++i)
                {
                    reader.ReadInt16(); // read any leftover unused data from legacy room files
                }
            }
            if (_loadedVersion >= RoomFileVersion.Version255B)
            {
                foreach (RoomRegion region in _loadingRoom.Regions)
                {
                    region.LightLevel = reader.ReadInt16();
                }
                foreach (RoomRegion region in _loadingRoom.Regions)
                {
                    const uint TINT_IS_ENABLED = 0x80000000;
                    int tint = reader.ReadInt32();
                    region.UseColourTint = ((tint & TINT_IS_ENABLED) != 0);
                    region.BlueTint = (tint >> 16) & 0x00FF;
                    region.GreenTint = (tint >> 8) & 0x00FF;
                    region.RedTint = tint & 0x00FF;
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
                LoadCompressedAllegro(reader, out bmp);
                _backgrounds[0].Graphic = bmp;
            }
            if ((_backgrounds[0].Graphic.Width > 320) && (_loadedVersion < RoomFileVersion.Version200Final))
            {
                _loadingRoom.Resolution = RoomResolution.HighRes;
            }
            if (_loadedVersion >= RoomFileVersion.Version255B)
            {
                LoadCompressedAllegro(reader, out _regionMask);
            }
            else if (_loadedVersion >= RoomFileVersion.Version114)
            {
                Bitmap bmp;
                LoadCompressedAllegro(reader, out bmp);
                // an old version, this mask is just deleted anyway
            }
            LoadCompressedAllegro(reader, out _walkableAreaMask);
            LoadCompressedAllegro(reader, out _walkBehindMask);
            LoadCompressedAllegro(reader, out _hotspotMask);
            if (_loadedVersion < RoomFileVersion.Version255B)
            {
                // old version - copy walkable areas to Regions
                if (_regionMask == null)
                {
                    _regionMask = new Bitmap(_walkableAreaMask);
                    _loadingRoom.Regions.Clear();
                    for (int i = 0; i < _loadingRoom.WalkableAreas.Count; ++i)
                    {
                        _loadingRoom.Regions.Add(new RoomRegion());
                        _loadingRoom.Regions[i].LightLevel = _legacyWalkableAreaLightLevel[i];
                        _loadingRoom.Regions[i].ID = i;
                    }
                }
            }
            return RoomLoadError.NoError;
        }

        private static string FileReadString(BinaryReader reader)
        {
            StringBuilder sb = new StringBuilder(300);
            for (byte b = reader.ReadByte(); b != 0; sb.Append(b), b = reader.ReadByte()) ;
            return sb.ToString();
        }

        private class CompiledScript
        {
            public const string FILE_SIGNATURE = "SCOM";
            public const int SCOM_VERSION = 89;
            public const uint END_FILE_SIG = 0xBEEFCAFE;

            List<byte> _globalData;
            List<int> _code;
            List<byte> _strings;
            List<byte> _fixUpTypes; // global data/string area/etc.
            List<int> _fixUps; // code array index to fix-up (in ints)
            List<string> _imports;
            List<string> _exports;
            List<int> _exportAddresses; // high byte is type; low 24-bits are offset
            //int _instances;
            // 'sections' allow the interpreter to find out which bit
            // of the code came from header files, and which from the main file
            List<string> _sectionNames;
            List<int> _sectionOffsets;

            public static CompiledScript CreateFromFile(BinaryReader reader)
            {
                CompiledScript script = new CompiledScript();
                return (script.Read(reader) ? script : null);
            }

            public bool Read(BinaryReader reader)
            {
                //_instances = 0;
                string gotSig = GetNullTerminatedASCIIString(reader.ReadBytes(4));
                int fileVer = reader.ReadInt32();
                if ((gotSig != FILE_SIGNATURE) || (fileVer > SCOM_VERSION))
                {
                    // TODO: log error "File was not written by CompiledScript.Write or Seek position is incorrect."
                    return false;
                }
                _globalData = new List<byte>(reader.ReadInt32());
                _code = new List<int>(reader.ReadInt32());
                _strings = new List<byte>(reader.ReadInt32());
                if (_globalData.Capacity > 0)
                {
                    _globalData.AddRange(reader.ReadBytes(_globalData.Capacity));
                }
                else _globalData = null;
                if (_code.Capacity > 0)
                {
                    byte[] bytes = reader.ReadBytes(_code.Capacity * sizeof(int));
                    for (int i = 0; i < _code.Capacity; ++i)
                    {
                        _code.Add(BitConverter.ToInt32(bytes, i * sizeof(int)));
                    }
                }
                else _code = null;
                if (_strings.Capacity > 0) _strings.AddRange(reader.ReadBytes(_strings.Capacity));
                else _strings = null;
                _fixUpTypes = new List<byte>(reader.ReadInt32());
                if (_fixUpTypes.Capacity > 0)
                {
                    _fixUps = new List<int>(_fixUpTypes.Capacity);
                    _fixUpTypes.AddRange(reader.ReadBytes(_fixUpTypes.Capacity));
                    byte[] bytes = reader.ReadBytes(_fixUps.Capacity * sizeof(int));
                    for (int i = 0; i < _fixUps.Capacity; ++i)
                    {
                        _fixUps.Add(BitConverter.ToInt32(bytes, i * sizeof(int)));
                    }
                }
                else
                {
                    _fixUpTypes = null;
                    _fixUps = null;
                }
                _imports = new List<string>(reader.ReadInt32());
                for (int i = 0; i < _imports.Capacity; ++i)
                {
                    _imports.Add(FileReadString(reader));
                }
                _exports = new List<string>(reader.ReadInt32());
                _exportAddresses = new List<int>(_exports.Capacity);
                for (int i = 0; i < _exports.Capacity; ++i)
                {
                    _exports.Add(FileReadString(reader));
                    _exportAddresses.Add(reader.ReadInt32());
                }
                if (fileVer >= 83)
                {
                    // read in the sections
                    _sectionNames = new List<string>(reader.ReadInt32());
                    _sectionOffsets = new List<int>(_sectionNames.Capacity);
                    for (int i = 0; i < _sectionNames.Capacity; ++i)
                    {
                        _sectionNames.Add(FileReadString(reader));
                        _sectionOffsets.Add(reader.ReadInt32());
                    }
                }
                else
                {
                    _sectionNames = null;
                    _sectionOffsets = null;
                }
                if (reader.ReadUInt32() != END_FILE_SIG)
                {
                    // TODO: log error "Internal error rebuilding script"
                    return false;
                }
                return true;
            }

            public string GetSectionName(int offset)
            {
                // TODO: implement or destroy
                return null;
            }
        }

        RoomLoadError ReadScriptBlock(BinaryReader reader)
        {
            const string password = RoomLoaderConstants.ROOM_PASSWORD;
            int scriptLength = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(scriptLength);
            for (int i = 0; i < scriptLength; ++i)
            {
                bytes[i] += (byte)password[i % password.Length];
            }
            _textScript = GetNullTerminatedASCIIString(bytes);
            return RoomLoadError.NoError;
        }

        RoomLoadError ReadObjectNamesBlock(BinaryReader reader)
        {
            const int MAX_OBJECT_NAME_LEN = 30;
            int count = reader.ReadByte();
            if (_loadedVersion > RoomFileVersion.Version340Alpha)
            {
                // allow more than 255 object names in room file
                // TODO: when writing room file save this count as Int32 not Byte (aka UInt8)
                byte[] bytes = new byte[4];
                reader.Read(bytes, 1, 3);
                bytes[0] = (byte)count;
                count = BitConverter.ToInt32(bytes, 0);
            }
            if (count != _loadingRoom.Objects.Count)
            {
                return RoomLoadError.InconsistentDataForObjectNames;
            }
            foreach (RoomObject obj in _loadingRoom.Objects)
            {
                obj.Description = GetNullTerminatedASCIIString(reader.ReadBytes(MAX_OBJECT_NAME_LEN));
            }
            return RoomLoadError.NoError;
        }

        RoomLoadError ReadAnimatedBackgroundBlock(BinaryReader reader)
        {
            _loadingRoom.BackgroundCount = reader.ReadByte();
            _backgrounds.Capacity = _loadingRoom.BackgroundCount;
            _loadingRoom.BackgroundAnimationDelay = reader.ReadByte();
            while (_backgrounds.Count < _loadingRoom.BackgroundCount)
            {
                _backgrounds.Add(new RoomBackground());
                _backgrounds[_backgrounds.Count - 1].ID = (_backgrounds.Count - 1);
            }
            if (_loadedVersion >= RoomFileVersion.Version255A)
            {
                foreach (RoomBackground bkg in _backgrounds)
                {
                    bkg.PaletteShared = (reader.ReadByte() != 0);
                }
            }
            foreach (RoomBackground bkg in _backgrounds)
            {
                if (bkg.ID == 0) continue;
                Bitmap bmp;
                List<Color> pal = bkg.Palette;
                LoadLZW(reader, out bmp, ref pal);
                bkg.Palette = pal;
                bkg.Graphic = bmp;
            }
            return RoomLoadError.NoError;
        }

        RoomLoadError ReadCompiledScriptBlock(BinaryReader reader)
        {
            _compiledScript = CompiledScript.CreateFromFile(reader);
            return (_compiledScript == null ? RoomLoadError.ScriptLoadFailed : RoomLoadError.NoError);
        }

        enum CustomPropertiesError
        {
            NoError,
            UnsupportedFormat
        };

        enum CustomPropertiesVersion
        {
            Pre340 = 1,
            Version340,
            Current = Version340
        };

        CustomPropertiesError UnserializeCustomProperties(BinaryReader reader, ref CustomProperties properties)
        {
            const int LEGACY_MAX_CUSTOM_PROPERTY_NAME_LENGTH = 200;
            const int LEGACY_MAX_CUSTOM_PROPERTY_VALUE_LENGTH = 500;
            CustomPropertiesVersion version = (CustomPropertiesVersion)reader.ReadInt32();
            if ((version < CustomPropertiesVersion.Pre340) || (version > CustomPropertiesVersion.Current))
            {
                return CustomPropertiesError.UnsupportedFormat;
            }
            properties.PropertyValues.Clear();
            int count = reader.ReadInt32(); // Dictionary<,> doesn't have a Capacity but it doesn't really matter to us here
            if (version == CustomPropertiesVersion.Pre340)
            {
                for (int i = 0; i < count; ++i)
                {
                    byte[] name = reader.ReadBytes(LEGACY_MAX_CUSTOM_PROPERTY_NAME_LENGTH);
                    byte[] value = reader.ReadBytes(LEGACY_MAX_CUSTOM_PROPERTY_VALUE_LENGTH);
                    CustomProperty property = new CustomProperty();
                    property.Name = GetNullTerminatedASCIIString(name);
                    property.Value = GetNullTerminatedASCIIString(value);
                    properties.PropertyValues.Add(property.Name, property);
                }
            }
            else
            {
                for (int i = 0; i < count; ++i)
                {
                    StringBuilder name = new StringBuilder(LEGACY_MAX_CUSTOM_PROPERTY_NAME_LENGTH);
                    StringBuilder value = new StringBuilder(LEGACY_MAX_CUSTOM_PROPERTY_VALUE_LENGTH);
                    for (byte b = reader.ReadByte(); b != 0; name.Append(b), b = reader.ReadByte()) ;
                    for (byte b = reader.ReadByte(); b != 0; value.Append(b), b = reader.ReadByte()) ;
                    CustomProperty property = new CustomProperty();
                    property.Name = name.ToString();
                    property.Value = value.ToString();
                    properties.PropertyValues.Add(property.Name, property);
                }
            }
            return CustomPropertiesError.NoError;
        }

        RoomLoadError ReadPropertiesBlock(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1) return RoomLoadError.PropertiesFormatNotSupported;
            CustomProperties properties = _loadingRoom.Properties;
            if (UnserializeCustomProperties(reader, ref properties) != CustomPropertiesError.NoError)
            {
                return RoomLoadError.PropertiesLoadFailed;
            }
            _loadingRoom.Properties.PropertyValues.Clear();
            foreach (CustomProperty property in properties.PropertyValues.Values)
            {
                _loadingRoom.Properties.PropertyValues.Add(property.Name, property);
            }
            foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
            {
                properties = hotspot.Properties;
                if (UnserializeCustomProperties(reader, ref properties) != CustomPropertiesError.NoError)
                {
                    return RoomLoadError.PropertiesLoadFailed;
                }
                else
                {
                    // by the time anything is read into our temporary, no errors are returned
                    // otherwise we'd need to copy this in case of error also
                    hotspot.Properties.PropertyValues.Clear();
                    // RoomHotspot.Properties setter isn't publicly accessible? urgh!
                    foreach (CustomProperty property in properties.PropertyValues.Values) // CustomProperties isn't directly enumerable?? wut?
                    {
                        hotspot.Properties.PropertyValues.Add(property.Name, property);
                    }
                }
            }
            foreach (RoomObject obj in _loadingRoom.Objects)
            {
                properties = obj.Properties;
                if (UnserializeCustomProperties(reader, ref properties) != CustomPropertiesError.NoError)
                {
                    return RoomLoadError.PropertiesLoadFailed;
                }
                else
                {
                    obj.Properties.PropertyValues.Clear();
                    foreach (CustomProperty property in properties.PropertyValues.Values)
                    {
                        obj.Properties.PropertyValues.Add(property.Name, property);
                    }
                }
            }
            return RoomLoadError.NoError;
        }

        RoomLoadError ReadObjectScriptNamesBlock(BinaryReader reader)
        {
            int count = reader.ReadByte();
            if (_loadedVersion > RoomFileVersion.Version340Alpha)
            {
                // allow more than 255 object names in room file
                // TODO: when writing room file save this count as Int32 not Byte (aka UInt8)
                byte[] bytes = new byte[4];
                reader.Read(bytes, 1, 3);
                bytes[0] = (byte)count;
                count = BitConverter.ToInt32(bytes, 0);
            }
            if (count != _loadingRoom.Objects.Count)
            {
                return RoomLoadError.InconsistentDataForObjectScriptNames;
            }
            foreach (RoomObject obj in _loadingRoom.Objects)
            {
                obj.Name = GetNullTerminatedASCIIString(reader.ReadBytes(RoomLoaderConstants.MAX_SCRIPT_NAME_LEN));
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
                    readBlockError = ReadScriptBlock(reader);
                    break;
                case RoomFormatBlock.CompiledScript:
                case RoomFormatBlock.CompiledScript2:
                    return RoomLoadError.OldBlockNotSupported;
                case RoomFormatBlock.ObjectNames:
                    readBlockError = ReadObjectNamesBlock(reader);
                    break;
                case RoomFormatBlock.AnimatedBackground:
                    readBlockError = ReadAnimatedBackgroundBlock(reader);
                    break;
                case RoomFormatBlock.CompiledScript3:
                    readBlockError = ReadCompiledScriptBlock(reader);
                    break;
                case RoomFormatBlock.Properties:
                    readBlockError = ReadPropertiesBlock(reader);
                    break;
                case RoomFormatBlock.ObjectScriptNames:
                    readBlockError = ReadObjectScriptNamesBlock(reader);
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

        void ProcessAfterRead(int roomNumber, bool gameIsHighRes)
        {
            _backgrounds[0].Palette.Clear();
            _backgrounds[0].Palette.AddRange(_palette);
            if ((_loadedVersion < RoomFileVersion.Version303B) && (gameIsHighRes))
            {
                // Pre-3.0.3, multiply up co-ordinates
                // If you change this, also change convert_room_coordinates_to_low res
                // function in the engine
                foreach (RoomObject obj in _loadingRoom.Objects)
                {
                    obj.StartX <<= 1;
                    obj.StartY <<= 1;
                    if (obj.Baseline > 0) obj.Baseline <<= 1;
                }
                foreach (RoomHotspot hotspot in _loadingRoom.Hotspots)
                {
                    hotspot.WalkToPoint = new Point(hotspot.WalkToPoint.X << 1, hotspot.WalkToPoint.Y << 1);
                }
                foreach (RoomWalkBehind walkBehind in _loadingRoom.WalkBehinds)
                {
                    walkBehind.Baseline <<= 1;
                }
                _loadingRoom.LeftEdgeX <<= 1;
                _loadingRoom.TopEdgeY <<= 1;
                _loadingRoom.BottomEdgeY <<= 1;
                _loadingRoom.RightEdgeX <<= 1;
                _loadingRoom.Width <<= 1;
                _loadingRoom.Height <<= 1;
            }
            if (_loadedVersion < RoomFileVersion.Version340Alpha)
            {
                _loadingRoom.StateSaving = (roomNumber <= Room.LEGACY_NON_STATE_SAVING_INDEX);
            }
        }

        private RoomLoadError ReadFromFile(BinaryReader reader, int roomNumber, bool gameIsHighRes, ref RoomFormatBlock lastBlock)
        {
            Free();
            if (reader == null) return RoomLoadError.InternalLogicError;
            InitializeDefaults();
            _loadingRoom = new Room(roomNumber);
            _loadedVersion = (RoomFileVersion)reader.ReadInt16();
            if ((_loadedVersion < RoomFileVersion.Version250B) || (_loadedVersion > RoomFileVersion.Current))
            {
                return RoomLoadError.FormatNotSupported;
            }
            RoomFormatBlock blockType = RoomFormatBlock.None;
            while (true)
            {
                blockType = (RoomFormatBlock)reader.ReadByte();
                lastBlock = blockType;
                if (blockType < 0) return RoomLoadError.UnexpectedEOF;
                if (blockType == RoomFormatBlock.End) break;
                RoomLoadError error = ReadBlock(reader, blockType);
                if (error != RoomLoadError.NoError)
                {
                    return error;
                }
            }
            ProcessAfterRead(roomNumber, gameIsHighRes);
            return RoomLoadError.NoError;
        }

        enum RoomOptions
        {
            StartUpMusic,
            SaveLoadDisabled,
            PlayerCharacterDisabled,
            PlayerCharacterView,
            MusicVolume,
            MAX_ROOM_OPTIONS = 10
        };

        public Room Load(UnloadedRoom roomToLoad)
        {
            string filename = roomToLoad.FileName;
            AGSEditor editor = AGSEditor.Instance;
            bool gameIsHighRes = (editor.CurrentGame.Settings.Resolution > GameResolutions.R320x240);
            FileStream fstream = File.Open(filename, FileMode.Open, FileAccess.Read);
            if (fstream == null)
            {
                throw new AGSEditorException("LoadRoom: Unable to load the room file '" + filename + "'\n" +
                                             "Make sure that you saved the room to the correct folder (it should be\n" +
                                             "in your game's project folder).\n" +
                                             "Also check that the player character's starting room is set correctly.\n");
            }
            BinaryReader reader = new BinaryReader(fstream);
            RoomFormatBlock lastBlock = RoomFormatBlock.None;
            RoomLoadError loadError = ReadFromFile(reader, roomToLoad.Number, gameIsHighRes, ref lastBlock);
            reader.BaseStream.Close();
            switch (loadError)
            {
                case RoomLoadError.InternalLogicError:
                    throw new AGSEditorException("LoadRoom: internal logic error.\n");
                case RoomLoadError.UnexpectedEOF:
                    throw new AGSEditorException("LoadRoom: unexpected end of file while loading room.\n");
                case RoomLoadError.FormatNotSupported:
                    throw new AGSEditorException("LoadRoom: Bad packed file. Either the file requires a newer or older version of\n" +
                                                 "this program or the file is corrupt.\n");
                case RoomLoadError.UnknownBlockType:
                    throw new AGSEditorException("LoadRoom: unknown block type " + ((int)lastBlock).ToString() + " encountered in '" + filename + "'.\n");
                case RoomLoadError.OldBlockNotSupported:
                    throw new AGSEditorException("LoadRoom: old room format. Please upgrade the room.");
                case RoomLoadError.InconsistentDataForObjectNames:
                    throw new AGSEditorException("LoadRoom: inconsistent blocks for object names.\n");
                case RoomLoadError.ScriptLoadFailed:
                    throw new AGSEditorException("LoadRoom: Script load failed; need newer version?\n");
                case RoomLoadError.PropertiesFormatNotSupported:
                    throw new AGSEditorException("LoadRoom: unknown Custom Properties block encountered.\n");
                case RoomLoadError.PropertiesLoadFailed:
                    throw new AGSEditorException("LoadRoom: error reading Custom Properties block.\n");
                case RoomLoadError.InconsistentDataForObjectScriptNames:
                    throw new AGSEditorException("LoadRoom: inconsistent blocks for object script names.\n");
                default:
                    break;
            }
            _loadingRoom.Description = roomToLoad.Description;
            _loadingRoom.Script = roomToLoad.Script;
            _loadingRoom.MusicVolumeAdjustment = (RoomVolumeAdjustment)_options[(int)RoomOptions.MusicVolume];
            _loadingRoom.PlayerCharacterView = _options[(int)RoomOptions.PlayerCharacterView];
            _loadingRoom.PlayMusicOnRoomLoad = _options[(int)RoomOptions.StartUpMusic];
            _loadingRoom.SaveLoadEnabled = (_options[(int)RoomOptions.SaveLoadDisabled] == 0);
            _loadingRoom.ShowPlayerCharacter = (_options[(int)RoomOptions.PlayerCharacterDisabled] == 0);
            foreach (RoomObject obj in _loadingRoom.Objects)
            {
                if (!string.IsNullOrEmpty(obj.Name))
                {
                    if (_loadedVersion < RoomFileVersion.Version300A)
                    {
                        StringBuilder jibbledScriptName = new StringBuilder(obj.Name.Length + 1);
                        jibbledScriptName.Append("o" + obj.Name.ToLower());
                        jibbledScriptName[1] = jibbledScriptName[1].ToString().ToUpper()[0];
                        obj.Name = jibbledScriptName.ToString();
                    }
                }
                if (_loadedVersion <= RoomFileVersion.Version300A)
                {
                    Sprite spr = editor.CurrentGame.RootSpriteFolder.FindSpriteByID(obj.Image, true);
                    if (spr != null) obj.StartY += spr.Height;
                }
            }
            foreach (RoomWalkableArea area in _loadingRoom.WalkableAreas)
            {
                int zoom = area.MinScalingLevel;
                int zoom2 = area.MaxScalingLevel;
                area.UseContinuousScaling = (zoom2 != RoomLoaderConstants.NOT_VECTOR_SCALED);
                area.ScalingLevel = zoom + 100;
                area.MinScalingLevel = zoom + 100;
                if (area.UseContinuousScaling)
                {
                    area.MaxScalingLevel = zoom2 + 100;
                }
                else area.MaxScalingLevel = area.MinScalingLevel;
            }
            foreach (RoomRegion region in _loadingRoom.Regions)
            {
                region.TintSaturation = (region.LightLevel > 0 ? region.LightLevel : 50);
                region.LightLevel += 100;
            }
            // TODO: room._roomStructPtr? this should probably be phased out
            // (from native) room->_roomStructPtr = (IntPtr)&thisroom;
            return _loadingRoom;
        }
    }
}
