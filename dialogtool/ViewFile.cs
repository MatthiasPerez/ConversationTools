﻿using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace dialogtool
{
    public static class ViewFile
    {
        //private static List<string> Colors = new List<string>(new string[] { "darkorange", "red", "blue", "darkorchid", "green", "olive" });
        private static List<string> Colors = new List<string>(new string[] { "darkorchid", "blue", "green", "olive", "darkorange", "red"});
        //Entity = key
        //Color = value
        private static Dictionary<string, string> EntityColor = new Dictionary<string, string>();

        public static void Write(Dialog dialog, string sourceFilePath)
        {

            ViewGenerator viewGenerator = new ViewGenerator(dialog);
            
            GetColorCode(dialog);

            var settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "   ";
            settings.OmitXmlDeclaration = true;

            using (var xw = XmlWriter.Create(sourceFilePath + ".html", settings))
            {
                xw.WriteStartElement("body");

                foreach (var intent in viewGenerator.Intents)
                {
                    //Sample of questions associated with the current intent
                    string questions = "";
                    for(int i = 0; i<5; i++)
                    {
                        questions += "-" + intent.Questions[i] + "\r\n";
                    }

                    xw.WriteStartElement("h1");
                    xw.WriteAttributeString("title", questions);
                    xw.WriteString(intent.Name);
                    xw.WriteEndElement(); // h1           

                    WriteColorCode(xw);

                    xw.WriteStartElement("table");

                    //table visual options
                    xw.WriteAttributeString("border", "3");
                    xw.WriteAttributeString("width", "80%");
                    xw.WriteAttributeString("cellpadding", "5");
                    xw.WriteAttributeString("cellspacing", "5"); 
                    xw.WriteAttributeString("bgcolor", "#E6E6E6");

                    foreach (var condition in intent.ViewNodes)
                    {
                        WriteRoot(condition, xw);
                    }

                    xw.WriteEndElement(); // table
                }

                xw.WriteEndElement(); // body
            }

        }

        //Write the root of the current ViewNode data-tree
        private static void WriteRoot(ViewNode condition, XmlWriter xw)
        {
            xw.WriteStartElement("tr");
            WriteNode(condition, xw);
            xw.WriteEndElement();
        }

        //Recursively write the nodes of the current ViewNode data-tree 
        private static void WriteNode(ViewNode condition, XmlWriter xw)
        {
            //We don't write the root node, or any empty cell
            if (condition.DisplayValues[0].Value != "root" && condition.DisplayValues[0].Value != "")
            {
                xw.WriteStartElement("td");

                xw.WriteAttributeString("RowSpan", GetRawSpan(condition).ToString());
                xw.WriteAttributeString("bgcolor", "#FAFAFA");

                if (condition.DisplayValues[0].Attributes.Count > 0)
                {
                    xw.WriteAttributeString(condition.DisplayValues[0].Attributes[0].Name, condition.DisplayValues[0].Attributes[0].Value);
                }

                if (condition.DisplayValues.Count > 0)
                {
                    foreach (var value in condition.DisplayValues)
                    {                     
                        xw.WriteStartElement("font");
                        if (value.Attributes.Count > 0)
                        {
                            foreach (var attribute in value.Attributes)
                            {
                                xw.WriteAttributeString(attribute.Name, attribute.Value);
                            }
                        }

                        xw.WriteAttributeString("color", GetColor(value));

                        xw.WriteString(value.Value);

                        if (condition.DisplayValues.Count > 1)
                        {
                            xw.WriteString(" | ");
                        }

                        xw.WriteEndElement(); //font
                    }
                }
                //if there's no child node left, it's the end of the data-tree --> 
                /*else
                {
                    xw.WriteStartElement("font");

                    xw.WriteAttributeString("color", GetColor(condition.DisplayValues[0]));

                    xw.WriteString(condition.DisplayValues[0].Value);

                    xw.WriteEndElement(); //font

                }*/

               
                xw.WriteEndElement(); //td
            }

            //As long as there's a child node, we keep going
            if (condition.Children != null && condition.Children.Count > 0)
            {
                foreach (var child in condition.Children)
                {
                    WriteNode(child, xw);
                }
            }

            //if there's no child left, it's a leaf, we end the row </tr> and start a new one <tr>
            if (condition.Children != null || condition.Children.Count > 0)
            {
                xw.WriteEndElement();
                xw.WriteStartElement("tr");

            }

        }

        //Get the number of leaves a node has
        private static int GetRawSpan(ViewNode condition)
        {

            int rowspan = ( GetNextChild(condition, 0) >=1) ? GetNextChild(condition, 0) : 1;

            return rowspan;

        }

        //used to recursively read the data-tree, summing-up the number of leaves
        private static int GetNextChild(ViewNode condition, int rowspan)
        {

            rowspan = condition.Children.Count;

            if (condition.Children != null && condition.Children.Count > 0)
            {
                foreach (var child in condition.Children)
                {                  
                    rowspan += GetNextChild(child, rowspan);
                }
            }

            return rowspan;
        }

        //Associate entity to a color code in the EntityColor Dictionnary
        private static void GetColorCode(Dialog dialog)
        {
            int i = 0;
            foreach (var entity in dialog.Entities)
            {
                EntityColor.Add(entity.Value.Name.TrimEnd("_ENTITY").ToLower(), Colors[i]);
                i += 1;
            }
        }

        //Write the color legend
        private static void WriteColorCode(XmlWriter xw)
        {
            xw.WriteStartElement("p");

            foreach (var entitycolor in EntityColor)
            {
                xw.WriteStartElement("font");
                xw.WriteAttributeString("color", entitycolor.Value);
                xw.WriteString(entitycolor.Key + " | ");
                xw.WriteEndElement(); // font

            }

            xw.WriteEndElement(); // p
        }

        //Colorize text with reference to the relevant entity
        private static string GetColor(DisplayValue displayValue)
        {
            string color;

            if (!EntityColor.TryGetValue(displayValue.Variable.TrimEnd("_Var").ToLower(), out color))
            {
                color = "black";
            }

            return color;

        }

        //TrimEnd() overload using a string instead of a char[]
        private static string TrimEnd(this string input, string suffixToRemove)
        {

            if (input != null && suffixToRemove != null
              && input.EndsWith(suffixToRemove))
            {
                return input.Substring(0, input.Length - suffixToRemove.Length);
            }
            else return input;
        }

    }
}