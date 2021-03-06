﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2018 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AvionicsSystems
{
    internal class MASActionGroup
    {
        internal enum ActionType
        {
            Activate,
            Deactivate,
            Toggle
        };

        /// <summary>
        /// Template describing the ACTION nodes of MAS_ACTION_GROUP.
        /// </summary>
        internal class MASActionTemplate
        {
            internal readonly string name;
            internal readonly string partName;
            internal readonly string module;
            internal readonly ActionType action;

            internal MASActionTemplate(ConfigNode config, string groupName)
            {
                if (!config.TryGetValue("name", ref name))
                {
                    name = "anonymous";
                }
                if (!config.TryGetValue("part", ref partName))
                {
                    throw new ArgumentException("Missing 'part' in ACTION " + name + " in MAS_ACTION_GROUP " + groupName);
                }
                // Convert to KSP canonical name
                // TODO: Verify that ' ' -> '.'
                partName = partName.Replace('_', '.').Replace(' ', '.');

                if (!config.TryGetValue("module", ref module))
                {
                    throw new ArgumentException("Missing 'module' in ACTION " + name + " in MAS_ACTION_GROUP " + groupName);
                }
                string actionString = string.Empty;
                if (!config.TryGetValue("action", ref actionString))
                {
                    throw new ArgumentException("Missing 'action' in ACTION " + name + " in MAS_ACTION_GROUP " + groupName);
                }
                switch (actionString.ToLower())
                {
                    case "on":
                        action = ActionType.Activate;
                        break;
                    case "off":
                        action = ActionType.Deactivate;
                        break;
                    case "toggle":
                        action = ActionType.Toggle;
                        break;
                    default:
                        throw new ArgumentException("Unrecognized 'action' \"" + actionString + "\" in ACTION " + name + " in MAS_ACTION_GROUP " + groupName);
                }
            }
        };

        /// <summary>
        /// Abstract class for MASActionGroup components.
        /// </summary>
        internal abstract class MASAction
        {
            internal readonly ActionType action;
            internal MASAction(ActionType action)
            {
                this.action = action;
            }

            internal abstract bool GetState();
            internal abstract void Toggle();
            internal abstract void SetState(bool newState);
        };

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleEngines.
        /// </summary>
        internal class MASActionGeneric : MASAction
        {
            PartModule module;
            Action<object> trigger;

            internal MASActionGeneric(PartModule module, Action<object> trigger, ActionType action)
                : base(action)
            {
                this.module = module;
                this.trigger = trigger;
            }

            internal override bool GetState()
            {
                return false;
            }

            internal override void Toggle()
            {
                trigger(module);
            }

            internal override void SetState(bool newState)
            {
                trigger(module);
            }
        }

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleLight.
        /// </summary>
        internal class MASActionModuleLight : MASAction
        {
            ModuleLight lightModule;

            internal MASActionModuleLight(ModuleLight lightModule, ActionType action)
                : base(action)
            {
                this.lightModule = lightModule;
            }

            internal override bool GetState()
            {
                return lightModule.isOn;
            }

            internal override void Toggle()
            {
                if (lightModule.isOn)
                {
                    if (action == ActionType.Toggle || action == ActionType.Deactivate)
                    {
                        lightModule.LightsOff();
                    }
                }
                else
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        lightModule.LightsOn();
                    }
                }
            }

            internal override void SetState(bool newState)
            {
                if (newState)
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        lightModule.LightsOn();
                    }
                }
                else
                {
                    if (action == ActionType.Toggle || action == ActionType.Deactivate)
                    {
                        lightModule.LightsOff();
                    }
                }
            }
        }

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleAnimateGeneric.
        /// </summary>
        internal class MASActionModuleAnimateGeneric : MASAction
        {
            ModuleAnimateGeneric animationModule;
            AnimationState animationState;

            internal MASActionModuleAnimateGeneric(ModuleAnimateGeneric animationModule, ActionType action)
                : base(action)
            {
                this.animationModule = animationModule;
                // ModuleAnimateGeneric.GetAnimation returns null at this point, so we must poll in Extended()
            }

            internal override bool GetState()
            {
                return Extended();
            }

            private bool Extended()
            {
                if (animationState == null)
                {
                    Animation animation = animationModule.GetAnimation();
                    animationState = animation[animationModule.animationName];
                }
                if (animationState != null && animationModule.IsMoving())
                {
                    return (animationState.normalizedSpeed > 0.0f);
                }
                else if (animationModule.Progress > 0.5f)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            internal override void Toggle()
            {
                if (animationModule.CanMove)
                {
                    if (Extended())
                    {
                        if (action == ActionType.Toggle || action == ActionType.Deactivate)
                        {
                            animationModule.Toggle();
                        }
                    }
                    else
                    {
                        if (action == ActionType.Toggle || action == ActionType.Activate)
                        {
                            animationModule.Toggle();
                        }
                    }
                }
            }

            internal override void SetState(bool newState)
            {
                if (animationModule.CanMove)
                {
                    if (newState && !Extended())
                    {
                        if (action == ActionType.Toggle || action == ActionType.Activate)
                        {
                            animationModule.Toggle();
                        }
                    }
                    else if (!newState && Extended())
                    {
                        if (action == ActionType.Toggle || action == ActionType.Deactivate)
                        {
                            animationModule.Toggle();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleDecouple.
        /// </summary>
        internal class MASActionModuleDecouple : MASAction
        {
            ModuleDecouple decoupleModule;

            internal MASActionModuleDecouple(ModuleDecouple decoupleModule, ActionType action)
                : base(action)
            {
                this.decoupleModule = decoupleModule;
            }

            internal override bool GetState()
            {
                return decoupleModule.isDecoupled;
            }

            internal override void Toggle()
            {
                if (!GetState())
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        decoupleModule.Decouple();
                    }
                }
            }

            internal override void SetState(bool newState)
            {
                if (newState && !GetState())
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        decoupleModule.Decouple();
                    }
                }
            }
        }

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleEngines.
        /// </summary>
        internal class MASActionModuleEngines : MASAction
        {
            ModuleEngines engineModule;

            internal MASActionModuleEngines(ModuleEngines engineModule, ActionType action)
                : base(action)
            {
                this.engineModule = engineModule;
            }

            internal override bool GetState()
            {
                return engineModule.getIgnitionState;
            }

            internal override void Toggle()
            {
                if (GetState())
                {
                    if (engineModule.allowShutdown && (action == ActionType.Toggle || action == ActionType.Deactivate))
                    {
                        engineModule.Shutdown();
                    }
                }
                else
                {
                    if (/*engineModule.allowRestart &&*/ (action == ActionType.Toggle || action == ActionType.Activate))
                    {
                        engineModule.Activate();
                    }
                }
            }

            internal override void SetState(bool newState)
            {
                Utility.LogMessage(this, "SetState({0})", newState);
                if (!newState && GetState())
                {
                    if (engineModule.allowShutdown && (action == ActionType.Toggle || action == ActionType.Deactivate))
                    {
                        Utility.LogMessage(this, "... Shutdown");
                        engineModule.Shutdown();
                    }
                }
                else if (newState && !GetState())
                {
                    if (/*engineModule.allowRestart &&*/ (action == ActionType.Toggle || action == ActionType.Activate))
                    {
                        Utility.LogMessage(this, "... Activate");
                        engineModule.Activate();
                    }
                }
            }
        }

        /// <summary>
        /// Interface class for MASActionGroup control of ModuleRCS.
        /// </summary>
        internal class MASActionModuleRCS : MASAction
        {
            ModuleRCS rcsModule;

            internal MASActionModuleRCS(ModuleRCS rcsModule, ActionType action)
                : base(action)
            {
                this.rcsModule = rcsModule;
            }

            internal override bool GetState()
            {
                return rcsModule.rcsEnabled;
            }

            internal override void Toggle()
            {
                if (GetState())
                {
                    if (action == ActionType.Toggle || action == ActionType.Deactivate)
                    {
                        rcsModule.rcsEnabled = false;
                    }
                }
                else
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        rcsModule.rcsEnabled = true;
                    }
                }
            }

            internal override void SetState(bool newState)
            {
                if (!newState && GetState())
                {
                    if (action == ActionType.Toggle || action == ActionType.Activate)
                    {
                        rcsModule.rcsEnabled = false;
                    }
                }
                else if (newState && !GetState())
                {
                    if (action == ActionType.Toggle || action == ActionType.Deactivate)
                    {
                        rcsModule.rcsEnabled = true;
                    }
                }
            }
        }

        // MASActionGroup implementation starts here.
        internal readonly string name;
        internal readonly int actionGroupId;
        private readonly MASActionTemplate[] actionTemplate;
        private MASAction[] action;

        internal MASActionGroup(ConfigNode config)
        {
            if (!config.TryGetValue("name", ref name))
            {
                name = "anonymous";
            }

            if (!config.TryGetValue("id", ref actionGroupId))
            {
                throw new ArgumentException("Missing 'id' in MAS_ACTION_GROUP " + name);
            }
            if (actionGroupId < 10)
            {
                throw new ArgumentException("'id' must be 10 or larger in MAS_ACTION_GROUP " + name);
            }

            ConfigNode[] actionNodes = config.GetNodes("ACTION");
            actionTemplate = new MASActionTemplate[actionNodes.Length];
            for (int i = 0; i < actionNodes.Length; ++i)
            {
                actionTemplate[i] = new MASActionTemplate(actionNodes[i], name);
            }
        }

        /// <summary>
        /// Rebuild the action array.
        /// </summary>
        /// <param name="parts"></param>
        internal void Rebuild(List<Part> parts)
        {
            List<MASAction> newAction = new List<MASAction>();

            //Utility.LogMessage(this, "Rebuild AG {0}", actionGroupId);
            //foreach (var aPart in parts)
            //{
            //    Utility.LogMessage(this, "... {0}", aPart.name);
            //}

            // I'm pretty sure this isn't the most efficient way to do this...
            int numTemplates = actionTemplate.Length;
            for (int templateIdx = 0; templateIdx < numTemplates; ++templateIdx)
            {
                // This is kind-of lame.  The part name for the root part has " (vesselName)" tacked to the end of it
                // (ie, "SomePart (Untitled Craft)"), so I can't do a simple string comparison.
                string templateName = actionTemplate[templateIdx].partName;
                List<Part> relevantParts = parts.FindAll((Part x) =>
                {
                    int idxSpace = x.name.IndexOf(' ');
                    if (idxSpace > 0)
                    {
                        return x.name.Substring(0, idxSpace) == templateName;
                    }
                    else
                    {
                        return x.name == templateName;
                    }
                });
                //Utility.LogMessage(this, "Found {0} parts of name {1}", relevantParts.Count, actionTemplate[templateIdx].partName);
                if (relevantParts.Count > 0)
                {
                    bool warned = false;
                    string[] reflectedAction = actionTemplate[templateIdx].module.Split('.');
                    foreach (Part p in relevantParts)
                    {
                        switch (actionTemplate[templateIdx].module)
                        {
                            case "ModuleAnimateGeneric":
                                List<ModuleAnimateGeneric> mag = p.FindModulesImplementing<ModuleAnimateGeneric>();
                                //Utility.LogMessage(this, "Found {0} ModuleAnimateGeneric", mag.Count);
                                foreach (ModuleAnimateGeneric magx in mag)
                                {
                                    newAction.Add(new MASActionModuleAnimateGeneric(magx, actionTemplate[templateIdx].action));
                                }
                                break;
                            case "ModuleDecouple":
                                List<ModuleDecouple> md = p.FindModulesImplementing<ModuleDecouple>();
                                //Utility.LogMessage(this, "Found {0} ModuleDecouple", md.Count);
                                foreach (ModuleDecouple mdx in md)
                                {
                                    newAction.Add(new MASActionModuleDecouple(mdx, actionTemplate[templateIdx].action));
                                }
                                break;
                            case "ModuleEngines":
                                List<ModuleEngines> me = p.FindModulesImplementing<ModuleEngines>();
                                //Utility.LogMessage(this, "Found {0} ModuleEngines", me.Count);
                                foreach (ModuleEngines mex in me)
                                {
                                    newAction.Add(new MASActionModuleEngines(mex, actionTemplate[templateIdx].action));
                                }
                                break;
                            case "ModuleLight":
                                List<ModuleLight> ml = p.FindModulesImplementing<ModuleLight>();
                                //Utility.LogMessage(this, "Found {0} ModuleLight", ml.Count);
                                foreach (ModuleLight mlx in ml)
                                {
                                    newAction.Add(new MASActionModuleLight(mlx, actionTemplate[templateIdx].action));
                                }
                                break;
                            case "ModuleRCS":
                                List<ModuleRCS> mr = p.FindModulesImplementing<ModuleRCS>();
                                //Utility.LogMessage(this, "Found {0} ModuleRCS", mr.Count);
                                foreach (ModuleRCS mrx in mr)
                                {
                                    newAction.Add(new MASActionModuleRCS(mrx, actionTemplate[templateIdx].action));
                                }
                                break;
                            default:
                                if (reflectedAction.Length != 2)
                                {
                                    if (!warned)
                                    {
                                        Utility.LogWarning(this, "Found unrecognized module \"{0}\" for MAS_ACTION_GROUP {1}", actionTemplate[templateIdx].module, name);
                                        warned = true;
                                    }
                                }
                                else if (p.Modules.Contains(reflectedAction[0]))
                                {
                                    PartModule mod = p.Modules[reflectedAction[0]];
                                    Type t = mod.GetType();
                                    MethodInfo mi = t.GetMethod(reflectedAction[1]);
                                    if (mi == null || mi.GetParameters().Length > 0)
                                    {
                                        if (!warned)
                                        {
                                            Utility.LogWarning(this, "Did not find method \"{3}\" with 0 parameters in module \"{0}\" in part \"{2}\" for MAS_ACTION_GROUP {1}",
                                                reflectedAction[0], name, p.name, reflectedAction[1]);
                                            warned = true;
                                        }
                                    }
                                    else
                                    {
                                        Action<object> theAction = DynamicMethodFactory.CreateAction<object>(mi);
                                        newAction.Add(new MASActionGeneric(mod, theAction, actionTemplate[templateIdx].action));
                                    }
                                }
                                else
                                {
                                    if (!warned)
                                    {
                                        Utility.LogWarning(this, "Did not find module \"{0}\" in part \"{2}\" for MAS_ACTION_GROUP {1}",
                                            reflectedAction[0], name, p.name);
                                        warned = true;
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            action = newAction.ToArray();
            //Utility.LogMessage(this, "Ended up with {0} entries in action.", action.Length);
        }

        /// <summary>
        /// Returns the action group state.  State is defined as the state of action[0].
        /// </summary>
        /// <returns></returns>
        internal bool GetState()
        {
            return (action.Length > 0) ? action[0].GetState() : false;
        }

        /// <summary>
        /// Returns true if there are any actions attached to this MAS AG.
        /// </summary>
        /// <returns></returns>
        internal bool HasActions()
        {
            return (action.Length > 0);
        }

        /// <summary>
        /// Toggle action group states.
        /// </summary>
        internal void Toggle()
        {
            if (action.Length > 0)
            {
                for (int i = 0; i < action.Length; ++i)
                {
                    action[i].Toggle();
                }
            }
        }

        /// <summary>
        /// Update the state of the action group.
        /// </summary>
        /// <param name="newState">The new state to select.</param>
        internal void SetState(bool newState)
        {
            if (action.Length > 0)
            {
                for (int i = 0; i < action.Length; ++i)
                {
                    action[i].SetState(newState);
                }
            }
        }
    }
}
