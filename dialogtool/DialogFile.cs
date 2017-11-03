﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace dialogtool
{
    public class DialogFile
    {
        // Default config parameters for CM-CIC insurance & savings dialogs
        static string startFolderLabel = "Main";
        static string intentsFolderLabel = "CM-CIC";
        static string answerFolderLabel = "AnswerNode";
        static string arrayOfAllowedVariablesPrefix = "ArrayOfAllowed";

        static Dictionary<string, string> intentCanonical = new Dictionary<string, string>();

        string filePath;
        XDocument XmlDocument;
        Dialog dialog;

        public DialogFile(FileInfo dialogFileInfo)
        {
            filePath = dialogFileInfo.FullName;
            XmlDocument = XDocument.Load("file://" + filePath, LoadOptions.SetLineInfo);
        }

        public Dialog Read(bool isInternalTest = false)
        {
            dialog = new Dialog(filePath);

            if (isInternalTest)
            {
                intentsFolderLabel = "TestLibrary";
            }
            else
            {
                var mainFolder = XmlDocument.Descendants("folder").Where(node => node.Attribute("label") != null && node.Attribute("label").Value == startFolderLabel).First();
                AnalyzeMainFolder(mainFolder);
                var answerFolder = XmlDocument.Descendants("folder").Where(node => node.Attribute("label") != null && node.Attribute("label").Value == answerFolderLabel).First();
                AnalyzeAnswerFolder(answerFolder);
            }
            AnalyzeVariablesFolders();
            AnalyzeConstantsFolders();
            AnalyzeEntitiesAndConceptsFolders();
            dialog.DetectMappingURIsConfig();
            MappingUriGenerator.FindInsuranceAllowedValue(XmlDocument);

            var rootIntentFolder = XmlDocument.Descendants("folder").Where(elt => elt.Attribute("label").Value == intentsFolderLabel).First();
            AnalyzeIntentsFolder(intentsFolderLabel, rootIntentFolder);
            dialog.ResolveAndCheckReferences();

            LogOfflineElements();

            ReadIntentCanonical();

            return dialog;
        }

        public Dictionary<string,string> ReadIntentCanonical()
        {
            var node = XmlDocument.Descendants("action").Where(attribute => attribute.Attribute("varName").Value == "ArrayOfCanonicalQuestions");
            
            foreach(var n in node)
            {
                string result = "<root>" +Regex.Replace(n.Value.ToString(), @"\r\n?|\n", "") + "</root>";
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(result);

                foreach (XmlNode encodedNode in xmlDoc)
                {
                    foreach (XmlNode encodedNode2 in encodedNode)
                    {
                        if (!intentCanonical.ContainsKey(encodedNode2.Name))
                            intentCanonical.Add(encodedNode2.Name, encodedNode2.InnerText);


                    }
                }
            }

            return intentCanonical;
        }

        private void LogOfflineElements()
        {
            var offlineElements = XmlDocument.Descendants().Where(node => node.Attribute("isOffline") != null);
            foreach (var offlineElement in offlineElements)
            {
                dialog.LogMessage(((IXmlLineInfo)offlineElement).LineNumber, MessageType.Info, "Element " + offlineElement.Name.LocalName + " is disabled, you should maybe delete it");
            }
        }

        private void AnalyzeMainFolder(XElement mainFolder)
        {
            /*
            <folder label="Main">
            <input>
                <grammar>
                    <item>__#testing_entities</item>
                </grammar>
                ...
            </input>
            <output>
                <prompt selectionType="RANDOM">
                    <item>[greetingTag_1][IntroTag_0]</item>
                </prompt>
                ..
                <output id="output_900001">
                    <prompt selectionType="RANDOM">
                        <item>[PromptTag_0]</item>
                    </prompt>
            */
            var startOfDialogNode = mainFolder.Element("output").Element("output");
            dialog.StartOfDialogNodeId = startOfDialogNode.Attribute("id").Value;

            /*
            <output id="output_900001">
	            <action varName="ArrayOfAllowedSupports" operator="SET_TO">[actions][actions_etrangeres]...</action>
	            <action varName="ArrayOfAllowedProducts" operator="SET_TO">[assurance_vie][autres_produits]...</action>
  	            <getUserInput>
  	                <action varName="Product_Var" operator="SET_TO_BLANK"/>
  	                ...	
                    <if>                           
			            <cond varName="federationGroup" operator="EQUALS">CM</cond>
                        <goto ref="search_200028">
                            <action varName="ArrayOfAllowedProducts" operator="APPEND">[bourse_plus]...</action>
                            <action varName="ArrayOfAllowedSupports" operator="APPEND">[fonds_a_formule]...</action>
                        </goto>                        
		            </if>                        
		            <if>                            
			            <cond varName="federationGroup" operator="EQUALS">CIC</cond>                            
                         ...
            */
            var arraysOfAllowedValuesNodes = startOfDialogNode.Descendants("action").Where(action => action.Attribute("varName").Value.StartsWith(arrayOfAllowedVariablesPrefix) && action.Attribute("operator").Value == "SET_TO");
            var arraysOfAllowedValues = new Dictionary<string, string[]>();
            foreach (var arrayOfAllowedValuesNode in arraysOfAllowedValuesNodes)
            {
                var arrayVarName = arrayOfAllowedValuesNode.Attribute("varName").Value;
                var entityNameFromArray = GetEntityNameFromAllowedValuesArrayName(arrayVarName);
                var allowedValues = arrayOfAllowedValuesNode.Value.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                arraysOfAllowedValues.Add(entityNameFromArray, allowedValues);
            }

            if (arraysOfAllowedValues.Keys.Count > 0)
            {
                var federationGroupsNodes = startOfDialogNode.Descendants("if").Where(ifNode => ifNode.Element("cond") != null && ifNode.Element("cond").Attribute("varName").Value == "federationGroup" && ifNode.Element("cond").Attribute("operator").Value == "EQUALS" && ifNode.Descendants("action").Any());
                dialog.ArraysOfAllowedValuesByEntityNameAndFederation = new Dictionary<string, IDictionary<string, IList<string>>>();
                foreach (var entityName in arraysOfAllowedValues.Keys)
                {
                    var allowedValuesForThisVariable = arraysOfAllowedValues[entityName];
                    var allowedValuesByFederation = new Dictionary<string, IList<string>>();
                    foreach (var federationGroupNode in federationGroupsNodes)
                    {
                        var federationGroup = federationGroupNode.Element("cond").Value.Trim();

                        var allowedValuesForThisVariableAndFederation = allowedValuesForThisVariable.ToList();
                        var arrayVarName = GetAllowedValuesArrayNameFromEntityName(entityName);
                        var specificValuesNode = federationGroupNode.Descendants("action").Where(action => action.Attribute("varName").Value.Equals(arrayVarName) && action.Attribute("operator").Value == "APPEND").FirstOrDefault();
                        if (specificValuesNode != null)
                        {
                            allowedValuesForThisVariableAndFederation.AddRange(specificValuesNode.Value.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries));
                        }
                        allowedValuesByFederation.Add(federationGroup, allowedValuesForThisVariableAndFederation);
                    }
                    dialog.ArraysOfAllowedValuesByEntityNameAndFederation.Add(entityName, allowedValuesByFederation);
                }
            }
        }

        private static string GetEntityNameFromAllowedValuesArrayName(string arrayVarName)
        {
            var entityLabel = arrayVarName.Substring(arrayOfAllowedVariablesPrefix.Length, arrayVarName.Length - arrayOfAllowedVariablesPrefix.Length - 1);
            var entityName = entityLabel.ToUpper() + "_ENTITY";
            return entityName;
        }

        private string GetAllowedValuesArrayNameFromEntityName(string entityName)
        {
            var entityLabel = entityName.Split('_')[0].ToLower();
            entityLabel = Char.ToUpper(entityLabel[0]) + entityLabel.Substring(1);
            var arrayVarName = arrayOfAllowedVariablesPrefix + entityLabel + "s";
            return arrayVarName;
        }

        private void AnalyzeAnswerFolder(XElement answerFolder)
        {
            /*
            <folder label="AnswerNode" id="folder_922993">
                <output id="output_922994">
                    ...
                    <output id="output_922827">
                        <prompt>
                            <item>{"mappingURI": "{mappingURI_var}","confidence":{CLASSIFIER_CONF_0}}</item>
                        </prompt>
                        <goto ref="output_900001"/>
                    </output>
                </output>
            </folder>
            */
            var firstId = answerFolder.Attribute("id").Value;
            var secondId = answerFolder.Element("output").Attribute("id").Value;
            dialog.FatHeadAnswerNodeIds = new string[] { firstId, secondId };
            dialog.LongTailAnswerNodeId = answerFolder.Element("output").Element("output").Attribute("id").Value;
        }

        private void AnalyzeVariablesFolders()
        {
            /*
            <variables>
                <var_folder name="Home">
                  <var_folder name="multiple_entities" type="VAR">
                    <var name="Event_Var_2" type="TEXT"/>
                  </var_folder>
                  <var name="REDIRECT_LONG_TAIL" type="YESNO" initValue="No"/>
                  <var name="CLASSIFIER_CONF_1" type="NUMBER" initValue="1e-05"/>
                  <var name="CLASSIFIER_CLASS_1" type="TEXT" initValue="Exact Match Returned"/>
             */
            var variablesNode = XmlDocument.Descendants("variables").First();
            foreach (var varNode in variablesNode.Descendants("var").Where(node => node.Attribute("isOffline") == null))
            {
                var name = varNode.Attribute("name").Value;

                var type = DialogVariableType.Text;
                var typeText = varNode.Attribute("type").Value;
                if (typeText.Equals("YESNO", StringComparison.InvariantCultureIgnoreCase))
                {
                    type = DialogVariableType.YesNo;
                }
                else if (typeText.Equals("NUMBER", StringComparison.InvariantCultureIgnoreCase))
                {
                    type = DialogVariableType.Number;
                }

                string initValue = null;
                if (varNode.Attribute("initValue") != null)
                {
                    initValue = varNode.Attribute("initValue").Value;
                }

                var variable = new DialogVariable(name, type, initValue);
                variable.LineNumber = ((IXmlLineInfo)varNode).LineNumber;
                dialog.AddVariable(variable);
            }
        }

        private void AnalyzeConstantsFolders()
        {
            /*
            <constants>
                <var_folder name="Home">
                    <var_folder name="Clarificaion Tags" type="CONST">
                        <var name="Domain_Clarification_Tag" type="TAG" description="Are you asking about Auto or Housing?">
                            Pourriez-vous préciser si votre question porte sur un contrat de la branche Auto ou de la branche Habitation &lt;ul&gt;
                            &lt;li data-auto-question="true"&gt;Auto&lt;/li&gt;
                            &lt;li data-auto-question="true"&gt;Habitation&lt;/li&gt;&lt;/ul&gt;
                        </var>
            */
            var constantsNode = XmlDocument.Descendants("constants").First();
            foreach (var constantNode in constantsNode.Descendants("var").Where(node => node.Attribute("isOffline") == null))
            {
                var constant = new Constant(constantNode.Attribute("name").Value, constantNode.Value);
                constant.LineNumber = ((IXmlLineInfo)constantNode).LineNumber;
                dialog.AddConstant(constant);
            }
        }

        private void AnalyzeEntitiesAndConceptsFolders()
        {
            /*
            <folder label="Concepts">
              <folder label="Insurance">
                <folder label="Products">
                  <folder label="Corail-4.14">
                    <concept description="Permet de regrouper toutes les variantes de désignation de ce contrat" id="concept_922535">
                      <grammar>
                        <item>Corail-4.14</item>
                        <item>Corail 4.14</item>
                        <item>Corail 4,14</item>
                      </grammar>
                    </concept>
            */
            var conceptNodes = XmlDocument.Descendants("folder").Where(node => node.Attribute("label") != null && node.Attribute("label").Value == "Concepts").Descendants("concept").Where(node => node.Attribute("isOffline") == null);
            foreach (var conceptNode in conceptNodes)
            {
                var synonyms = conceptNode.Descendants("item").Select(item => item.Value.Trim()).ToList();
                string conceptId = null;
                if (conceptNode.Attribute("id") != null)
                {
                    conceptId = conceptNode.Attribute("id").Value;
                }
                var concept = new Concept(conceptId, synonyms);
                concept.LineNumber = ((IXmlLineInfo)conceptNode).LineNumber;
                dialog.AddConcept(concept);
            }
            dialog.OnAllConceptsAdded();

            /*
            <entities>
                <entity name="PRODUCT_ENTITY" entityType="GENERIC">
                    <value name="a15" value="a15">
                        <concept ref="concept_921875"/>
                    </value>
                    <value name="a15_tiers_etendue" value="a15-tiers-etendue"/>
                    <value name="a15_tous_risques_optimale" value="a15-tous-risques-optimale"/>
                    <value name="a15_tous_risques_standard" value="a15-tous-risques-standard"/>
            */
            var entityNodes = XmlDocument.Descendants("entity").Where(node => node.Attribute("isOffline") == null);
            foreach (var entityNode in entityNodes)
            {
                var entity = new Entity(entityNode.Attribute("name").Value);
                entity.LineNumber = ((IXmlLineInfo)entityNode).LineNumber;
                foreach (var valueNode in entityNode.Descendants("value").Where(node => node.Attribute("isOffline") == null))
                {
                    var canonicalValue = valueNode.Attribute("value").Value.Trim();
                    var entityValueName = valueNode.Attribute("name").Value;
                    var entityValue = new EntityValue(entity, entityValueName, canonicalValue);
                    entityValue.LineNumber = ((IXmlLineInfo)valueNode).LineNumber;
                    entity.AddEntityValue(entityValue, dialog);

                    IList<string> conceptIds = new List<string>();
                    if (valueNode.Descendants("concept").Any())
                    {
                        foreach (var conceptNode in valueNode.Descendants("concept"))
                        {
                            conceptIds.Add(conceptNode.Attribute("ref").Value);
                        }
                    }
                    dialog.LinkEntityValueToConcept(entityValue, conceptIds);
                }
                dialog.AddEntity(entity);
            }
            dialog.OnAllEntitiesAdded();
        }

        private void AnalyzeIntentsFolder(string parentFolder, XElement intentsFolder)
        {
            /*
            folder/folder
            */
            foreach (var childIntentsFolder in intentsFolder.Elements("folder").Where(elt => elt.Attribute("isOffline") == null))
            {
                AnalyzeIntentsFolder(childIntentsFolder.Attribute("label").Value, childIntentsFolder);
            }

            /*
            folder/input
            */
            foreach (var intentInput in intentsFolder.Elements("input").Where(elt => elt.Attribute("isOffline") == null))
            {
                AnalyzeMatchIntentAndEntities(parentFolder, intentInput, new DialogVariablesSimulator(dialog.Variables, MappingUriGenerator.GetEntityVariables(dialog.MappingUriConfig)));
            }
        }

        private void AnalyzeMatchIntentAndEntities(string parentFolder, XElement intentInput, DialogVariablesSimulator dialogVariables)
        {
            /*            
            input/grammar/item/()
            input/input
            input/action/()
            input/goto
            input/if
            input/output
            */

            // Intent and sample questions
            string intentName = null;
            var intentQuestions = new List<string>();
            bool isFirstItem = true;
            foreach (var item in intentInput.Element("grammar").Elements("item"))
            {
                if (isFirstItem)
                {
                    isFirstItem = false;
                    intentName = item.Value;

                    // Very special case : TBPs are ignored
                    if (intentName.Equals("TBPs", StringComparison.InvariantCultureIgnoreCase))
                    {
                        dialog.LogMessage(((IXmlLineInfo)intentInput).LineNumber, MessageType.Info, "Ignored \"" + intentName + "\" intent node");
                        return;
                    }
                }
                else
                {
                    intentQuestions.Add(item.Value);
                }
            }
            var intent = new MatchIntentAndEntities(parentFolder, intentName);
            SetDialogNodeIdAndLineNumberAndVariableAssignments(intent, intentInput, intentInput, dialogVariables, dialog);
            intent.Questions = intentQuestions;

            // Analyze entity matches
            bool isFirstChild = true;
            XElement switchSecondIfElement = null;
            DialogNode inlineSwitchDialogNode = null;
            foreach (var inputChildElement in intentInput.Elements("input").Where(elt => elt.Attribute("isOffline") == null))
            {
                var entityMatch = AnalyzeEntityMatch(intent, inputChildElement, dialogVariables, isFirstChild, ref inlineSwitchDialogNode, ref switchSecondIfElement);
                if (entityMatch != null)
                {
                    intent.AddEntityMatch(entityMatch);
                }

                // Check for unexpected additional output node
                if (inputChildElement.Element("output") != null)
                {
                    dialog.LogMessage(((IXmlLineInfo)inputChildElement.Element("output")).LineNumber, MessageType.IncorrectPattern, "Invalid pattern detected : unexpected output node inside entity match section");
                }
            }
            dialog.AddIntent(intent, dialogVariables);

            // Match entities and check their value
            foreach (var inputChildElement in intentInput.Elements().Where(elt => elt.Attribute("isOffline") == null))
            {
                if (inputChildElement == switchSecondIfElement)
                {
                    AnalyzeSwitchLoopOnce(inlineSwitchDialogNode, switchSecondIfElement);
                    inlineSwitchDialogNode = null;
                    continue;
                }
                switch (inputChildElement.Name.LocalName)
                {
                    case "grammar":
                    case "action":
                    case "input":
                        continue;
                    case "if":
                        AnalyzeDialogVariableConditions(inlineSwitchDialogNode == null ? intent : inlineSwitchDialogNode, inputChildElement, dialogVariables, isFirstChild, ref inlineSwitchDialogNode, ref switchSecondIfElement);
                        break;
                    case "output":
                        bool skipGotoPattern;
                        if (DetectDisambiguationQuestion(inputChildElement, out skipGotoPattern, dialog))
                        {
                            AnalyzeDisambiguationQuestion(intent, inputChildElement, dialogVariables);
                        }
                        else if (!skipGotoPattern)
                        {
                            AnalyzeGotoOrAnswerNode(intent, inputChildElement, dialogVariables);
                        }
                        break;
                    default:
                        throw new Exception("Line " + ((IXmlLineInfo)inputChildElement).LineNumber + " : Unexpected child element " + inputChildElement.Name + " below <input>");
                }
                isFirstChild = false;
            }
        }

        private void AnalyzeSwitchLoopOnce(DialogNode parentNode, XElement switchSecondIfElement)
        {
            var switchLoopOnce = new SwitchLoopOnce(parentNode);
            switchLoopOnce.LineNumber = ((IXmlLineInfo)switchSecondIfElement).LineNumber;
            if (switchSecondIfElement.Attribute("id") != null)
            {
                switchLoopOnce.Id = switchSecondIfElement.Attribute("id").Value;
                dialog.RegisterDialogNode(switchLoopOnce);
            }
            parentNode.ChildrenNodes.Add(switchLoopOnce);
        }

        private EntityMatch AnalyzeEntityMatch(DialogNode dialogNode, XElement inputElement, DialogVariablesSimulator dialogVariables, bool isFirstElement, ref DialogNode inlineSwitchDialogNode, ref XElement switchSecondIfElement)
        {
            // Extract element names
            string entityName = null;
            string variableName1 = null;
            XElement item1 = null;
            string variableName2 = null;
            XElement item2 = null;
            var grammarItems = inputElement.Element("grammar").Elements("item");
            foreach (var item in grammarItems)
            {
                int matchIndex = 0;
                foreach (var obj in ENTITY_MATCH_REGEX.Matches(item.Value))
                {
                    var match = (Match)obj;
                    if (entityName == null)
                    {
                        entityName = match.Groups["entity"].Value;
                    }
                    else if (entityName != match.Groups["entity"].Value)
                    {
                        throw new Exception("Line " + ((IXmlLineInfo)item).LineNumber + " : Incoherent EntityMatch pattern");
                    }
                    var varName = match.Groups["var"].Value;
                    if (matchIndex == 0)
                    {
                        variableName1 = varName;
                        item1 = item;
                    }
                    else if (matchIndex == 1)
                    {
                        variableName2 = varName;
                        item2 = item;
                    }
                    matchIndex++;
                }
            }
            if (entityName == null)
            {
                // Special case : direct text patterns instead of entity match => not supported yet
                // Is it a pattern we would like to support in the future or a mistake in the dialog file ?
                /*
                    <input>
                        <grammar>
                            <item>Transfert entrant externe</item>
                        </grammar>
                        <output>
                            <prompt/>
                            <action varName="Event_Var" operator="SET_TO">transfert_entrant_externe</action>
                            <goto ref="profileCheck_205586"/>
                        </output>
                    </input>
                 */
                dialog.LogMessage(((IXmlLineInfo)inputElement.Element("grammar")).LineNumber, MessageType.IncorrectPattern, "Invalid pattern detected : expected entity match, found direct text pattern");
                return null;
            }

            // Check pattern correctness
            bool variableName1IsExtracted = false;
            bool variableName2IsExtracted = false;
            var actions = inputElement.Elements("action");
            foreach (var action in actions)
            {
                var varName = action.Attribute("varName").Value;
                if (variableName1 == varName)
                {
                    variableName1IsExtracted = true;
                }
                else if (variableName2 == varName)
                {
                    variableName2IsExtracted = true;
                }
            }
            if (variableName1 != null && !variableName1IsExtracted)
            {
                dialog.LogMessage(((IXmlLineInfo)item1).LineNumber, MessageType.IncorrectPattern, "Matched entity " + entityName + " but did not store its name correctly in " + variableName1);
                variableName1 = null;
            }
            if (variableName2 != null && !variableName2IsExtracted)
            {
                dialog.LogMessage(((IXmlLineInfo)item2).LineNumber, MessageType.IncorrectPattern, "Matched entity " + entityName + " but did not store its name correctly in " + variableName2);
                variableName2 = null;
            }
            if (variableName1 == null && variableName2 != null)
            {
                variableName1 = variableName2;
                variableName2 = null;
            }

            var entityMatch = new EntityMatch(entityName, variableName1, variableName2);
            entityMatch.LineNumber = ((IXmlLineInfo)inputElement).LineNumber;
            dialog.LinkEntityMatchToEntityAndDialogVariables(dialogNode, entityMatch);

            // Handle if children nodes inside entity match pattern
            if (inputElement.Element("if") != null || inputElement.Element("goto") != null)
            {
                var listOfArtificialConditions = new List<DialogVariableCondition>();
                var artificialCondition = new DialogVariableCondition(variableName1, ConditionComparison.HasValue, null);
                listOfArtificialConditions.Add(artificialCondition);
                var artificialIfRootNode = new DialogVariableConditions(dialogNode, listOfArtificialConditions, ConditionOperator.Or);
                artificialIfRootNode.LineNumber = inputElement.Element("if") != null ? ((IXmlLineInfo)inputElement.Element("if")).LineNumber : ((IXmlLineInfo)inputElement.Element("goto")).LineNumber;

                foreach (var element in inputElement.Elements())
                {
                    if (element.Name.LocalName == "if")
                    {
                        AnalyzeDialogVariableConditions(artificialIfRootNode, element, dialogVariables, isFirstElement, ref inlineSwitchDialogNode, ref switchSecondIfElement);
                    }
                    else if (element.Name.LocalName == "goto" && element.Attribute("ref") != null)
                    {
                        var gotoRef = element.Attribute("ref").Value;
                        if (!inputElement.Parent.Elements("input").Where(e => e.Attribute("id") != null && e.Attribute("id").Value == gotoRef).Any())
                        {
                            AnalyzeGotoOrAnswerNode(artificialIfRootNode, element, dialogVariables);
                        }
                    }
                }
                if (artificialIfRootNode.ChildrenNodes != null && artificialIfRootNode.ChildrenNodes.Count > 0)
                {
                    dialogNode.ChildrenNodes.Add(artificialIfRootNode);
                }
            }

            return entityMatch;
        }

        private static Regex ENTITY_MATCH_REGEX = new Regex(@"\((?<entity>[^\)]+)\)\s*=\s*{(?<var>[^}]+)}", RegexOptions.Compiled);

        private void SetDialogNodeIdAndLineNumberAndVariableAssignments(DialogNode dialogNode, XElement idElement, XElement variablesElement, DialogVariablesSimulator dialogVariables, Dialog dialog)
        {
            SetDialogNodeIdAndLineNumberAndVariableAssignments(dialogNode, idElement, new XElement[] { variablesElement }, dialogVariables, dialog);
        }

        private void SetDialogNodeIdAndLineNumberAndVariableAssignments(DialogNode dialogNode, XElement idElement, XElement[] variablesElements, DialogVariablesSimulator dialogVariables, Dialog dialog)
        {
            dialogNode.LineNumber = ((IXmlLineInfo)idElement).LineNumber;

            if (idElement.Attribute("id") != null)
            {
                dialogNode.Id = idElement.Attribute("id").Value;
                dialog.RegisterDialogNode(dialogNode);
            }

            XElement previousVariableElement = null;
            foreach (var variablesElement in variablesElements)
            {
                if (variablesElement != null && variablesElement != previousVariableElement)
                {
                    var actions = variablesElement.Elements("action");
                    foreach (var action in actions)
                    {
                        var variableName = action.Attribute("varName").Value;
                        var operatorName = action.Attribute("operator").Value;
                        DialogVariableOperator @operator;
                        switch (operatorName)
                        {
                            case "SET_TO":
                                if(action.Value == "protection_juridique_corail_4.14")
                                {
                                    //Console.WriteLine(action.Value);
                                }
                                @operator = DialogVariableOperator.SetTo;
                                break;
                            case "SET_TO_BLANK":
                                @operator = DialogVariableOperator.SetToBlank;
                                break;
                            case "SET_TO_YES":
                                @operator = DialogVariableOperator.SetToYes;
                                break;
                            case "SET_TO_NO":
                                @operator = DialogVariableOperator.SetToNo;
                                break;
                            case "SET_AS_USER_INPUT":
                            case "APPEND":
                                dialog.LogMessage(((IXmlLineInfo)action).LineNumber, MessageType.IncorrectPattern, "Action with operator " + operatorName + " ignored while reading the Xml dialog file");
                                continue;
                            default:
                                throw new Exception("Line " + ((IXmlLineInfo)action).LineNumber + " : Unexpected action operator " + operatorName);
                        }
                        var variableValue = action.Value.Trim();
                        // Check for variable reference expression
                        if (variableValue.StartsWith("{") && variableValue.EndsWith("}"))
                        {
                            if (variableValue.EndsWith(":name}"))
                            {
                                dialog.LogMessage(((IXmlLineInfo)action).LineNumber, MessageType.IncorrectPattern, "Variable internal field to variable value assignment is not supported outside a MatchEntity pattern : " + variableValue + " => " + variableName);
                                variableValue = null;
                            }
                            else
                            {
                                var refVarName = variableValue.Substring(1, variableValue.Length - 2);
                                DialogVariable refVariable = null;
                                dialog.Variables.TryGetValue(refVarName, out refVariable);
                                if (refVariable == null)
                                {
                                    dialog.LogMessage(((IXmlLineInfo)action).LineNumber, MessageType.InvalidReference, "Failed to resolve variable name in variable to variable value assignment : " + refVarName);
                                }
                                else
                                {
                                    var copyFromVariable = new DialogVariableAssignment(variableName, DialogVariableOperator.CopyValueFromVariable, refVarName);
                                    dialog.LinkVariableAssignmentToVariable(dialogNode, copyFromVariable);
                                    if (dialogVariables.AddDialogVariableAssignment(copyFromVariable, dialogNode.Type))
                                    {
                                        dialogNode.AddVariableAssignment(copyFromVariable);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var variableAssignment = new DialogVariableAssignment(variableName, @operator, variableValue);
                            dialog.LinkVariableAssignmentToVariable(dialogNode, variableAssignment);
                            if (dialogVariables.AddDialogVariableAssignment(variableAssignment, dialogNode.Type))
                            {
                                dialogNode.AddVariableAssignment(variableAssignment);
                            }
                        }
                    }
                }
                previousVariableElement = variablesElement;
            }
        }

        private bool DetectDisambiguationQuestion(XElement outputElement, out bool skipGotoPattern, IMessageCollector errors)
        {
            skipGotoPattern = false;
            if (outputElement.Element("getUserInput") != null)
            {
                return true;
            }
            else
            {
                if (outputElement.Element("prompt") != null &&
                   outputElement.Element("prompt").Element("item") != null &&
                   outputElement.Element("input") != null)
                {
                    skipGotoPattern = true;
                    errors.LogMessage(((IXmlLineInfo)outputElement.Element("input")).LineNumber, MessageType.IncorrectPattern, "Disambiguation question pattern WITHOUT getUsetInput node !");
                }
                return false;
            }
        }

        private void AnalyzeDisambiguationQuestion(DialogNode parentNode, XElement outputElement, DialogVariablesSimulator dialogVariables)
        {
            /*                
            output/action/()
            output/getUserInput
                getUserInput/()
                getUserInput/action/()
                getUserInput/goto
                getUserInput/input
                getUserInput/output
            output/goto
            output/if
            output/input
            output/output
            output/prompt
                prompt/()
                prompt/item/()
            input/grammar/item/()
            input/input
            input/action/()
            input/goto
            input/if
            input/output    
            */

            // Analyze question text and options
            var promptElement = outputElement.Element("prompt");
            var getUserInput = outputElement.Element("getUserInput");
            string questionExpression;
            Constant constant;
            string questionText;
            AnalyzePromptMessage(promptElement, out questionExpression, out constant, out questionText);
            var match = HTML_DIALOG_OPTIONS_REGEX.Match(questionText);
            string[] options = null;
            if (match.Success)
            {
                var optionCount = match.Groups["option"].Captures.Count;
                options = new string[optionCount];
                for (int i = 0; i < optionCount; i++)
                {
                    options[i] = match.Groups["option"].Captures[i].Value;
                }
                questionText = HTML_DIALOG_OPTIONS_REGEX.Replace(questionText, "");
            }

            var disambiguationQuestion = new DisambiguationQuestion(parentNode, questionExpression, questionText);
            SetDialogNodeIdAndLineNumberAndVariableAssignments(disambiguationQuestion, outputElement, getUserInput, dialogVariables, dialog);
            parentNode.ChildrenNodes.Add(disambiguationQuestion);
            if (constant != null)
            {
                constant.AddDialogNodeReference(disambiguationQuestion);
            }

            // Analyze entity match and disambiguation options
            bool isFirstChild = true;
            XElement switchSecondIfElement = null;
            DialogNode inlineSwitchDialogNode = null;
            EntityMatch entityMatch = null;
            var inputElement = getUserInput.Element("input");
            if (inputElement != null)
            {
                entityMatch = AnalyzeEntityMatch(disambiguationQuestion, inputElement, dialogVariables, isFirstChild, ref inlineSwitchDialogNode, ref switchSecondIfElement);
                if (entityMatch != null)
                {
                    disambiguationQuestion.SetEntityMatchAndDisambiguationOptions(entityMatch, options, dialog);
                }
            }
            dialogVariables.AddDisambiguationQuestion(disambiguationQuestion);

            // Children nodes of disambiguation question
            var childrenNodes = getUserInput.Elements();
            AnalyzeDisambiguationQuestionChildren(dialogVariables, disambiguationQuestion, childrenNodes);

            // Optional last output node after getUserInput
            var lastOutputElement = outputElement.Element("output");
            if (lastOutputElement != null)
            {
                bool skipGotoPattern;
                if (DetectDisambiguationQuestion(lastOutputElement, out skipGotoPattern, dialog))
                {
                    AnalyzeDisambiguationQuestion(disambiguationQuestion, lastOutputElement, dialogVariables);
                }
                else if (!skipGotoPattern)
                {
                    AnalyzeGotoOrAnswerNode(disambiguationQuestion, lastOutputElement, dialogVariables);
                }
            }
        }

        private void AnalyzeDisambiguationQuestionChildren(DialogVariablesSimulator dialogVariables, DisambiguationQuestion disambiguationQuestion, IEnumerable<XElement> childrenNodes)
        {
            bool isFirstChild = true;
            XElement switchSecondIfElement = null;
            DialogNode inlineSwitchDialogNode = null;
            foreach (var getUserInputChildElement in childrenNodes.Where(elt => elt.Attribute("isOffline") == null))
            {
                if (getUserInputChildElement == switchSecondIfElement)
                {
                    AnalyzeSwitchLoopOnce(inlineSwitchDialogNode, switchSecondIfElement);
                    inlineSwitchDialogNode = null;
                    continue;
                }
                switch (getUserInputChildElement.Name.LocalName)
                {
                    case "input":
                    case "action":
                    case "grammar":
                        continue;
                    case "if":
                        AnalyzeDialogVariableConditions(inlineSwitchDialogNode == null ? disambiguationQuestion : inlineSwitchDialogNode, getUserInputChildElement, dialogVariables, isFirstChild, ref inlineSwitchDialogNode, ref switchSecondIfElement);
                        break;
                    case "output":
                    case "goto":
                        AnalyzeGotoOrAnswerNode(disambiguationQuestion, getUserInputChildElement, dialogVariables);
                        break;
                    case "folder":
                        AnalyzeDisambiguationQuestionChildren(dialogVariables, disambiguationQuestion, getUserInputChildElement.Elements());
                        break;
                    default:
                        throw new Exception("Line " + ((IXmlLineInfo)getUserInputChildElement).LineNumber + " : Unexpected child element " + getUserInputChildElement.Name + " below <getUserInput>");
                }
                isFirstChild = false;
            }
        }

        private static Regex HTML_DIALOG_OPTIONS_REGEX = new Regex(@"\s*<ul>\s*(<li\s*(data-auto-question=""true"")?\s*>(?<option>.+)</li>\s*)+</ul>\s*", RegexOptions.Compiled);

        private void AnalyzePromptMessage(XElement promptElement, out string messageExpression, out Constant constant, out string messageText)
        {
            messageExpression = null;
            constant = null;
            messageText = null;
            var promptItem = promptElement.Element("item");
            if (promptItem != null)
            {
                messageExpression = promptElement.Element("item").Value;
                var constantMatch = CONSTANT_REFERENCE_REGEX.Match(messageExpression);
                if (constantMatch.Success)
                {
                    var constantName = constantMatch.Groups["constant"].Value;
                    constant = dialog.TryGetConstant(((IXmlLineInfo)promptItem).LineNumber, constantName);
                    if (constant != null)
                    {
                        messageText = CONSTANT_REFERENCE_REGEX.Replace(messageExpression, constant.Value);
                    }
                    else
                    {
                        messageText = messageExpression;
                    }
                }
            }
        }

        private static Regex CONSTANT_REFERENCE_REGEX = new Regex(@"\[\s*(?<constant>[^\s\]]+)\s*\]", RegexOptions.Compiled);

        private void AnalyzeGotoOrAnswerNode(DialogNode parentNode, XElement outputOrGotoElement, DialogVariablesSimulator dialogVariables)
        {
            /*
           goto/()
           goto/action/()
           goto/if 
           */

            string messageExpression = null;
            Constant constant = null;
            string messageText = null;
            XElement gotoElement = null;

            var elementWithId = outputOrGotoElement;
            if (outputOrGotoElement.Name.LocalName == "output")
            {
                // Special case : pattern output / output / goto
                if (outputOrGotoElement.Element("output") != null)
                {
                    outputOrGotoElement = outputOrGotoElement.Element("output");
                }

                var promptElement = outputOrGotoElement.Element("prompt");
                AnalyzePromptMessage(promptElement, out messageExpression, out constant, out messageText);
                gotoElement = outputOrGotoElement.Element("goto");
            }
            else
            {
                gotoElement = outputOrGotoElement;
            }

            var gotoRef = String.Empty;
            if (gotoElement != null)
            {
                if (gotoElement.Attribute("ref") != null)
                {
                    gotoRef = gotoElement.Attribute("ref").Value;
                }
            }
            DialogNode gotoOrAnswerNode = null;
            if (dialog.StartOfDialogNodeId == gotoRef)
            {
                gotoOrAnswerNode = new DirectAnswer(parentNode, gotoRef, messageExpression, messageText, dialog);
            }
            else if (dialog.FatHeadAnswerNodeIds.Contains(gotoRef))
            {
                gotoOrAnswerNode = new FatHeadAnswers(parentNode, gotoRef, messageExpression, messageText, dialog);
            }
            else if (dialog.LongTailAnswerNodeId == gotoRef)
            {
                gotoOrAnswerNode = new RedirectToLongTail(parentNode, gotoRef, messageExpression, messageText, dialog);
            }
            else
            {
                gotoOrAnswerNode = new GotoNode(parentNode, gotoRef, messageExpression, messageText, dialog);
            }

            // Unsupported output pattern
            if (gotoElement == null && outputOrGotoElement.Element("if") != null)
            {
                dialog.LogMessage(((IXmlLineInfo)outputOrGotoElement).LineNumber, MessageType.IncorrectPattern, "Output node pattern different from disambiguation question or goto not supported => part of tree ignored");
                return;
            }

            SetDialogNodeIdAndLineNumberAndVariableAssignments(gotoOrAnswerNode, elementWithId, new XElement[] { outputOrGotoElement, gotoElement }, dialogVariables, dialog);
            parentNode.ChildrenNodes.Add(gotoOrAnswerNode);
            if (constant != null)
            {
                constant.AddDialogNodeReference(gotoOrAnswerNode);
            }
            if (gotoOrAnswerNode.Type == DialogNodeType.FatHeadAnswers)
            {
                ((FatHeadAnswers)gotoOrAnswerNode).GenerateMappingUris(dialogVariables, dialog.MappingUriConfig, dialog.ArraysOfAllowedValuesByEntityNameAndFederation, XmlDocument, gotoOrAnswerNode);
            }
            if (gotoOrAnswerNode.Type == DialogNodeType.GotoNode)
            {
                ((GotoNode)gotoOrAnswerNode).CheckTargetNodeId(dialog);
            }
        }

        private void AnalyzeDialogVariableConditions(DialogNode parentNode, XElement ifElement, DialogVariablesSimulator dialogVariables, bool isFirstElement, ref DialogNode inlineSwitchDialogNode, ref XElement switchSecondIfElement)
        {
            /*
            if/cond/()        
            if/if
            if/action/()
            if/output
            if/goto
            */

            // Conditional branch => clone variable values
            dialogVariables = dialogVariables.Clone();

            // Operator
            ConditionOperator @operator = ConditionOperator.Or;
            if (ifElement.Attribute("matchType") != null)
            {
                var matchType = ifElement.Attribute("matchType").Value;
                if (matchType.Equals("ANY", StringComparison.InvariantCultureIgnoreCase))
                {
                    @operator = ConditionOperator.Or;
                }
                else if (matchType.Equals("ALL", StringComparison.InvariantCultureIgnoreCase))
                {
                    @operator = ConditionOperator.And;
                }
            }

            // Conditions
            IList<DialogVariableCondition> variableConditions = new List<DialogVariableCondition>();
            foreach (var condition in ifElement.Elements("cond"))
            {
                var varName = condition.Attribute("varName").Value;

                var comparison = ConditionComparison.Equals;
                var comparisonName = condition.Attribute("operator").Value;
                if (comparisonName.Equals("HAS_VALUE", StringComparison.InvariantCultureIgnoreCase))
                {
                    comparison = ConditionComparison.HasValue;
                }
                else if (comparisonName.Equals("EQUALS", StringComparison.InvariantCultureIgnoreCase))
                {
                    comparison = ConditionComparison.Equals;
                }

                string value = condition.Value;

                var variableCondition = new DialogVariableCondition(varName, comparison, value);
                variableConditions.Add(variableCondition);
            }


            DialogNode dialogNode = null;

            // Special case : SwitchOnEntityVariables pattern
            // -> PATTERN 1 : enclosing HasValue condition
            if (variableConditions.Count == 1)
            {
                var uniqueCondition = variableConditions[0];
                if (uniqueCondition.Comparison == ConditionComparison.HasValue)
                {
                    EntityMatch relatedEntityMatch = null;
                    foreach (var entityMatch in dialogVariables.LastEntityMatches)
                    {
                        if (uniqueCondition.VariableName == entityMatch.EntityVariableName1)
                        {
                            relatedEntityMatch = entityMatch;
                            break;
                        }
                    }
                    if (relatedEntityMatch != null)
                    {
                        var lastIfChild = ifElement.Elements("if").LastOrDefault();
                        if (lastIfChild != null)
                        {
                            var cond = lastIfChild.Element("cond");
                            if (cond != null)
                            {
                                if (cond.Attribute("varName").Value == relatedEntityMatch.EntityVariableName2 &&
                                    cond.Attribute("operator").Value == "HAS_VALUE")
                                {
                                    switchSecondIfElement = lastIfChild;

                                    var switchOnEntityVariables = new SwitchOnEntityVariables(parentNode, relatedEntityMatch);
                                    SetDialogNodeIdAndLineNumberAndVariableAssignments(switchOnEntityVariables, ifElement, ifElement, dialogVariables, dialog);
                                    parentNode.ChildrenNodes.Add(switchOnEntityVariables);
                                    dialogNode = switchOnEntityVariables;
                                }
                            }
                        }
                    }
                }
            }
            // -> PATTERN 2 : no enclosing HasValue condition
            if (dialogNode == null && parentNode.Type != DialogNodeType.SwitchOnEntityVariables && isFirstElement && variableConditions.Count > 0)
            {
                var firstCondition = variableConditions[0];
                if (firstCondition.Comparison == ConditionComparison.Equals)
                {
                    EntityMatch relatedEntityMatch = null;
                    foreach (var entityMatch in dialogVariables.LastEntityMatches)
                    {
                        if (firstCondition.VariableName == entityMatch.EntityVariableName1)
                        {
                            relatedEntityMatch = entityMatch;
                            break;
                        }
                    }
                    if (relatedEntityMatch != null)
                    {
                        var lastIfSibling = ifElement.Parent.Elements("if").LastOrDefault();
                        if (lastIfSibling != null)
                        {
                            var cond = lastIfSibling.Element("cond");
                            if (cond != null)
                            {
                                if (cond.Attribute("varName").Value == relatedEntityMatch.EntityVariableName2 &&
                                    cond.Attribute("operator").Value == "HAS_VALUE")
                                {
                                    switchSecondIfElement = lastIfSibling;

                                    var switchOnEntityVariables = new SwitchOnEntityVariables(parentNode, relatedEntityMatch);
                                    SetDialogNodeIdAndLineNumberAndVariableAssignments(switchOnEntityVariables, ifElement, ifElement, dialogVariables, dialog);
                                    parentNode.ChildrenNodes.Add(switchOnEntityVariables);
                                    inlineSwitchDialogNode = switchOnEntityVariables;
                                    parentNode = inlineSwitchDialogNode;
                                }
                            }
                        }
                    }
                }
            }

            // DialogVariableConditions node
            if (dialogNode == null)
            {
                // Dialog node
                var dialogVariablesConditions = new DialogVariableConditions(parentNode, variableConditions, @operator);
                if (ifElement.Attribute("id") != null && ifElement.Attribute("id").Value == parentNode.Id)
                {
                    ifElement.SetAttributeValue("id", null);
                }
                SetDialogNodeIdAndLineNumberAndVariableAssignments(dialogVariablesConditions, ifElement, ifElement, dialogVariables, dialog);
                parentNode.ChildrenNodes.Add(dialogVariablesConditions);
                dialogNode = dialogVariablesConditions;

                // Add conditions
                foreach (var variableCondition in variableConditions)
                {
                    dialog.LinkDialogVariableConditionToDialogVariableAndEntityValue(dialogVariablesConditions, variableCondition, dialogVariables);
                }
                dialogVariables.AddDialogVariableConditions(dialogVariablesConditions);

                // Check for value restriction by federationGroup
                if (dialog.ArraysOfAllowedValuesByEntityNameAndFederation != null)
                {
                    if (ifElement.Element("if") != null && ifElement.Element("if").Element("cond") != null &&
                        ifElement.Element("if").Element("cond").Attribute("varName").Value.StartsWith(arrayOfAllowedVariablesPrefix) &&
                        ifElement.Element("if").Element("cond").Attribute("operator").Value == "CONTAINS")
                    {
                        // Jump to inner if element
                        ifElement = ifElement.Element("if");
                        // Look at additional values restriction
                        var restrictionCond = ifElement.Element("cond");
                        var arrayVarName = restrictionCond.Attribute("varName").Value;
                        var entityVarExpression = restrictionCond.Value.Trim();
                        if (!(entityVarExpression.StartsWith("[{") && entityVarExpression.EndsWith("}]")))
                        {
                            dialog.LogMessage(((IXmlLineInfo)restrictionCond).LineNumber, MessageType.IncorrectPattern, "Variable value expression should be of the form [{Product_Var}] instead of : " + entityVarExpression);
                        }
                        else
                        {
                            var entityVarName = entityVarExpression.Substring(2, entityVarExpression.Length - 4);
                            string entityLabel = arrayVarName.Substring(arrayOfAllowedVariablesPrefix.Length, arrayVarName.Length - arrayOfAllowedVariablesPrefix.Length - 1);
                            if (!entityVarName.StartsWith(entityLabel))
                            {
                                dialog.LogMessage(((IXmlLineInfo)restrictionCond).LineNumber, MessageType.IncorrectPattern, "Mismatch between array of allowed values name : " + arrayVarName + ", and entity variable name : " + entityVarName);
                            }
                            else
                            {
                                var entityNameFromArray = GetEntityNameFromAllowedValuesArrayName(arrayVarName);
                                IDictionary<string, IList<string>> allowedValuesByFederationGroup = null;
                                if (dialog.ArraysOfAllowedValuesByEntityNameAndFederation.TryGetValue(entityNameFromArray, out allowedValuesByFederationGroup))
                                {
                                    var variableValueRestriction = new DialogVariableCheck(entityVarName, allowedValuesByFederationGroup);
                                    dialogVariablesConditions.AddVariableValuesRestriction(variableValueRestriction, dialog);
                                }
                                else
                                {
                                    dialog.LogMessage(((IXmlLineInfo)restrictionCond).LineNumber, MessageType.InvalidReference, "Reference to an unknown array of allowed values: " + arrayVarName);
                                }
                            }
                        }
                    }

                    // Check variable values restrictions are applied each time a restricted entity value is tested in a condition
                    foreach (var condition in dialogVariablesConditions.VariableConditions)
                    {
                        if (condition.EntityValue != null && condition.EntityValue.AllowedInFederationGroups != null &&
                            condition.EntityValue.AllowedInFederationGroups.Count < dialog.ArraysOfAllowedValuesByEntityNameAndFederation[condition.EntityValue.Entity.Name].Keys.Count() &&
                            dialogVariablesConditions.VariableValuesRestriction == null &&
                            dialogVariables.TryGetVariableValue("federationGroup") == null)
                        {
                            dialog.LogMessage(dialogVariablesConditions.LineNumber, MessageType.IncorrectPattern, "Dialog variable condition references entity value : " + condition.EntityValue.Entity.Name + " > " + condition.EntityValue.Name + ", this value is not allowed in all federation groups : add a new \"if\" node below the current condition to check if the entity value is allowed for the current federation group");
                            break;
                        }
                    }
                }
            }

            // Children nodes of dialog variables conditions
            bool isFirstChild = true;
            XElement switchSecondIfElementLevel2 = null;
            DialogNode inlineSwitchDialogNode2 = null;
            foreach (var ifChildElement in ifElement.Elements().Where(elt => elt.Attribute("isOffline") == null))
            {
                if (ifChildElement == switchSecondIfElement)
                {
                    AnalyzeSwitchLoopOnce(dialogNode, switchSecondIfElement);
                    continue;
                }
                if (ifChildElement == switchSecondIfElementLevel2)
                {
                    AnalyzeSwitchLoopOnce(inlineSwitchDialogNode2, switchSecondIfElementLevel2);
                    inlineSwitchDialogNode2 = null;
                    continue;
                }
                switch (ifChildElement.Name.LocalName)
                {
                    case "cond":
                    case "action":
                        continue;
                    case "if":
                        AnalyzeDialogVariableConditions(inlineSwitchDialogNode2 == null ? dialogNode : inlineSwitchDialogNode2, ifChildElement, dialogVariables, isFirstChild, ref inlineSwitchDialogNode2, ref switchSecondIfElementLevel2);
                        break;
                    case "output":
                        bool skipGotoPattern;
                        if (DetectDisambiguationQuestion(ifChildElement, out skipGotoPattern, dialog))
                        {
                            AnalyzeDisambiguationQuestion(dialogNode, ifChildElement, dialogVariables);
                        }
                        else if (!skipGotoPattern)
                        {
                            AnalyzeGotoOrAnswerNode(dialogNode, ifChildElement, dialogVariables);
                        }
                        break;
                    case "goto":
                        AnalyzeGotoOrAnswerNode(dialogNode, ifChildElement, dialogVariables);
                        break;
                    default:
                        throw new Exception("Line " + ((IXmlLineInfo)ifChildElement).LineNumber + " : Unexpected child element " + ifChildElement.Name + " below <if>");
                }
                isFirstChild = false;
            }
        }

        public void Write(Dialog dialog, FileInfo templateFileInfo, FileInfo dialogFileInfo)
        {

        }
    }
}