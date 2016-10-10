using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;

//Jimmacle provided the original LCD classes. These are heavily modified :P

namespace ShipyardMod.ItemClasses
{
    public class LCDMenu
    {
        public IMyButtonPanel Buttons;
        private MenuItem currentItem;
        public IMyTextPanel Panel;
        private int selectedItemIndex;

        public LCDMenu()
        {
            Root = new MenuItem("Root", null);
            Root.root = this;
            SetCurrentItem(Root);
        }

        public MenuItem Root { get; set; }

        public void BindButtonPanel(IMyButtonPanel btnpnl)
        {
            //if (Buttons != null)
            //{
            //    Buttons.ButtonPressed -= ButtonPanelHandler;
            //}
            Buttons = btnpnl;
            //Buttons.ButtonPressed += ButtonPanelHandler;
            var blockDef = (MyButtonPanelDefinition)MyDefinitionManager.Static.GetCubeBlockDefinition(Buttons.BlockDefinition);
            Buttons.SetEmissiveParts("Emissive1", blockDef.ButtonColors[1 % blockDef.ButtonColors.Length], 1);
            Buttons.SetEmissiveParts("Emissive2", blockDef.ButtonColors[2 % blockDef.ButtonColors.Length], 1);
            Buttons.SetEmissiveParts("Emissive3", blockDef.ButtonColors[3 % blockDef.ButtonColors.Length], 1);
            Buttons.SetEmissiveParts("Emissive4", blockDef.ButtonColors[4 % blockDef.ButtonColors.Length], 1);
        }

        public void BindLCD(IMyTextPanel txtpnl)
        {
            if (Panel != null)
            {
                Panel.WritePublicText("MENU UNBOUND");
            }
            Panel = txtpnl;
            UpdateLCD();
        }

        public void SetCurrentItem(MenuItem item)
        {
            currentItem = item;
            selectedItemIndex = 0;
        }

        public void ButtonPanelHandler(int button)
        {
            switch (button)
            {
                case 0:
                    MenuActions.UpLevel(this, currentItem.Items[selectedItemIndex]);
                    break;
                case 1:
                    if (selectedItemIndex > 0)
                    {
                        selectedItemIndex--;
                    }
                    else
                        selectedItemIndex = currentItem.Items.Count - 1;
                    break;
                case 2:
                    if (selectedItemIndex < currentItem.Items.Count - 1)
                    {
                        selectedItemIndex++;
                    }
                    else
                        selectedItemIndex = 0;
                    break;
                case 3:
                    currentItem.Items[selectedItemIndex].Invoke();
                    break;
            }
            UpdateLCD();
        }

        public void UpdateLCD()
        {
            if (Panel != null)
            {
                var sb = new StringBuilder();

                sb.Append(currentItem.UpdateDesc() ?? currentItem.Description);
                for (int i = 0; i < currentItem.Items.Count; i++)
                {
                    if (i == selectedItemIndex)
                    {
                        sb.Append("[ " + currentItem.Items[i].Name + " ]" + '\n');
                    }
                    else
                    {
                        sb.Append("  " + currentItem.Items[i].Name + '\n');
                    }
                }
                string result = sb.ToString();
                //saves bandwidth and render time
                if (Panel.GetPublicText() == result)
                    return;
                Panel.WritePublicText(result);
                Panel.ShowPrivateTextOnScreen();
                Panel.ShowPublicTextOnScreen();
            }
        }

        public void WriteHugeString(string message)
        {
            Panel.WritePublicText("");
            int charcount = 4200; //KEEN

            for (int i = 0; i < message.Length * charcount; i++)
            {
                string substring = message.Substring(i * charcount, Math.Min(charcount, message.Length - i * charcount));
                Panel.WritePublicText(substring, true);
            }
        }
    }

    public class MenuItem
    {
        public delegate string DescriptionAction();

        public delegate void MenuAction();

        public LCDMenu root;

        public MenuItem(string name, string desc, MenuAction action = null, DescriptionAction descAct = null)
        {
            Name = name;
            Action = action;
            Description = desc;
            DescAction = descAct;
            Items = new List<MenuItem>();
        }

        public MenuAction Action { get; set; }

        public DescriptionAction DescAction { get; set; }

        public MenuItem Parent { get; private set; }
        public List<MenuItem> Items { get; set; }

        public string Name { get; }
        public string Description { get; set; }

        public void Add(MenuItem child)
        {
            child.root = root;
            child.Parent = this;
            Items.Add(child);
        }

        public void Invoke()
        {
            if (Action != null)
                Action.Invoke();
        }

        public string UpdateDesc()
        {
            if (DescAction != null)
                return DescAction.Invoke();
            return null;
        }
    }

    public static class MenuActions
    {
        public static void DownLevel(LCDMenu root, MenuItem item)
        {
            root.SetCurrentItem(item);
        }

        public static void UpLevel(LCDMenu root, MenuItem item)
        {
            if (item.Parent.Parent != null)
            {
                root.SetCurrentItem(item.Parent.Parent);
            }
        }
    }
}