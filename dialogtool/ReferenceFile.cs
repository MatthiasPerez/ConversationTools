using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;

namespace dialogtool
{
    public class ReferenceFile
    {
        private static string[] listFederation = { "CM", "CIC", "CMNE", "CMMABN", "CMO","FAG" };
        private static string[] listFederationString = { "CREDIT MUTUEL", "CIC", "CREDIT MUTUEL NORD EUROPE", "CREDIT MUTUEL MAINE ANJOU BASSE NORMANDIE",
            "CREDIT MUTUEL OCEAN", "Fédération Antilles Guyanne" };

        // The list of all the possibl entities
        private static string[] entityTypes =
        {
            "subdomain_entity",
            "support_entity",
            "event_entity",
            "person_entity",
            "product_entity",
            "history_entity",
            "object_entity",
            "guarantee_entity"
        };

        public static List<string> uris { get; private set; }
        public static string filePath;



        public static void Write(List<string> mappingURISet, string refFilePath, Dictionary<string, string> listIntentCanon )
        {
            Console.WriteLine("");
            Console.WriteLine("Write Reference File");

            uris = mappingURISet;
            filePath = refFilePath;

            // Write in the XML File
            var file = new FileInfo(refFilePath);
            List<RefRow> rows = new List<RefRow>();

            int xIntent  = 1;
            int xTakeIntoAccount = 2;
            int xCodeIntent = 3;
            int xVersion = 4;
            int xTargetIntent = 5;
            int xListEntitiesTypes = 6;
            int xNbEntity = 7;
            int xNbFede = 8;
            int xNumFede = 10;

            int xFirstEntity = 19;
            int xNumEntity = 30;
            int xUri = xFirstEntity + xNumEntity;

            

            // For each URI
            for(int i = 0; i < uris.Count; i ++)  
            {
                
                // Add a / for segmentation
                uris[i] = uris[i] + "/";
                // Intent
                string intent = StringUtils.ExtractFromString(uris[i], "intent/", "/")[0];

                string withoutFede = uris[i].Substring(uris[i].IndexOf("/intent") + 1);
                withoutFede = withoutFede.Remove(withoutFede.Length - 1);


                bool alreadyExist = false;
                foreach(var row in rows)
                {
                    if (withoutFede == row.uri)
                    {
                        alreadyExist = true;
                        break;
                    }
                }

                // If this URI has already been processed
                if (!alreadyExist)
                {
                    //entity
                    Dictionary<string, string> matchedEntity = new Dictionary<string, string>();
                    // Manage the entities
                    foreach (string entity in entityTypes)
                    {
                        List<String> value = StringUtils.ExtractFromString(uris[i], entity + "/", "/");
                        if (value != null)
                            matchedEntity.Add(entity, value[0]);
                    }
                    uris[i] = uris[i].Remove(uris[i].Length - 1);

                    //federations
                    Dictionary<string, string> matchedFederation = new Dictionary<string, string>(){
                        { "CM",""},
                        { "CIC","" },
                        { "CMNE",""},
                        { "CMMABN",""},
                        { "CMO",""},
                        { "FAG",""}
                    };
                    // Manage the federations
                    for (int j = 0; j < uris.Count; j ++)
                    {
                        string withoutFede2 = uris[j].Substring(uris[j].IndexOf("/intent") + 1);
                        if (withoutFede == withoutFede2)
                        {
                            string fede = StringUtils.ExtractFromString(uris[j], "/federationGroup/", "/intent")[0];
                            matchedFederation[fede] = "1";
                        }
                    }

                    // Remove the / at the end
                    if(withoutFede[withoutFede.Length - 1].ToString() == "/")
                        withoutFede = withoutFede.Remove(withoutFede.Length - 1);

                    rows.Add(new RefRow(intent, withoutFede, matchedEntity, matchedFederation));
                }

            }
            if (file.Exists) file.Delete();
            using (var package = new ExcelPackage(file))
            {
                // add all the worksheets
                package.Workbook.Worksheets.Add("Count Variations");
                package.Workbook.Worksheets.Add("Versionning XLS");
                package.Workbook.Worksheets.Add("Ref Versions-Business");
                ExcelWorksheet refFede = package.Workbook.Worksheets.Add("Ref Fédé");


                refFede.Cells[1, 1].Value = "Num Federation (onglet dialogues)";
                refFede.Cells[1, 2].Value = "Code";
                refFede.Cells[1, 3].Value = "Fédérations Assistant Santé";
                for (int i = 0; i < listFederation.Length; i++)
                {
                    refFede.Cells[i+2, 1].Value = i + 1;
                    refFede.Cells[i + 2, 2].Value = listFederation[i];
                    refFede.Cells[i + 2, 3].Value = listFederationString[i];

                }

                // The worksheet we need to change
                ExcelWorksheet worksheet = package.Workbook.Worksheets.Add("Dialogues");
                package.Workbook.Worksheets.Add("MAPPING URI TOTAL");
                package.Workbook.Worksheets.Add("Entities");
                package.Workbook.Worksheets.Add("Question clarification Entités");


                // Mapping intent - canonical question
                ExcelWorksheet intentCanon = package.Workbook.Worksheets.Add("IntentCanon-Dialogue Monitoring");

                int rowIntentCanon = 1;
                foreach( var intcan in listIntentCanon)
                {
                    intentCanon.Cells[rowIntentCanon, 1].Value = intcan.Key;
                    intentCanon.Cells[rowIntentCanon, 13].Value = intcan.Value;
                    rowIntentCanon++;
                }


                worksheet.Cells[1, xIntent].Value = "Intentions";
                worksheet.Cells[1, xTakeIntoAccount].Value = "Take into account";
                
                worksheet.Cells[1, xCodeIntent].Value = "Code Intent";
                worksheet.Cells[1, xVersion].Value = "Version";

                worksheet.Cells[1, xTargetIntent].Value = "Target Intent";
                worksheet.Cells[1, xListEntitiesTypes].Value = "Entities";
                worksheet.Cells[1, xNbEntity].Value = "# of entities";
                worksheet.Cells[1, xNbFede].Value = "Federations";


                for(int i = xNbFede + 1; i < xNumFede + xNbFede + 1; i ++)
                {
                    worksheet.Cells[1, i].Value = "Fédé " + (i - xNbFede);
                }

                for (int i = xFirstEntity; i < xNumEntity + xFirstEntity; i++)
                {
                    worksheet.Cells[1, i].Value = "entity " + (i - xFirstEntity + 1);
                }

                worksheet.Cells[1, xUri].Value = "Mapping URI ";

                int currentRow = 2;
                var oldRowIntent = "";
                int currentRowHeader = 2;

                string concatEntitiesForHeader = "";
                int nbEntitesForHeader = 0;

                // Generate all rows
                foreach(var row in rows)
                {
                    // Put the header
                    if(oldRowIntent != row.intent)
                    {
                        // Insert list entites and amount of last header
                        worksheet.Cells[currentRowHeader, xNbEntity].Value = nbEntitesForHeader;
                        worksheet.Cells[currentRowHeader, xListEntitiesTypes].Value = concatEntitiesForHeader;
                        

                        for(int k = currentRowHeader + 1; k < currentRow; k ++)
                        {
                            worksheet.Cells[k, xListEntitiesTypes].Value = concatEntitiesForHeader;
                        }

                        // New header   
                        concatEntitiesForHeader = "";
                        nbEntitesForHeader = 0;
                        currentRowHeader = currentRow;
                        worksheet.Cells[currentRowHeader, xIntent].Value = row.intent;
                        currentRow++;
                    }

                    oldRowIntent = row.intent;
                    worksheet.Cells[currentRow, xTakeIntoAccount].Value = "Y";
                    // Write intent
                    worksheet.Cells[currentRow, xIntent].Value = row.intent;

                    // Write the number of entities
                    worksheet.Cells[currentRow, xNbEntity].Value = row.entities.Count;

                    // Write federation
                    int countFede = 0;
                    int actualFedeRow = xNbFede + 1;
                    foreach(var f in row.federations)
                    {
                        if(f.Value == "1")
                        {
                            countFede++;
                        }
                    }
                    if(countFede == 5)
                    {
                        worksheet.Cells[currentRow, xNbFede].Value = "All";
                    }
                    else
                    {
                        worksheet.Cells[currentRow, xNbFede].Value = "Specific";
                        foreach (var f in row.federations)
                        {
                            if (f.Value == "1")
                            {
                                worksheet.Cells[currentRow, actualFedeRow].Value = 1;
                            }
                            actualFedeRow++;
                        }
                    }
                    string concat =  "";
                    foreach (var e in row.entities)
                    {
                        concat += e.Key + "/";

                        if(!concatEntitiesForHeader.Contains(e.Key))
                        {
                            concatEntitiesForHeader += e.Key + "/";
                            nbEntitesForHeader++;
                        }

                        int column = xFirstEntity + Array.IndexOf(entityTypes, e.Key);
                        worksheet.Cells[currentRow, column].Value = e.Value.ToLower();
                        worksheet.Cells[currentRowHeader, column].Value = e.Key;
                    }
                    worksheet.Cells[currentRow, xListEntitiesTypes].Value = concat;

                    worksheet.Cells[currentRow, xUri].Value = "/federationGroup/<? $federationGroup ?>/" + row.uri.ToLower();
                    currentRow++;
                }
                // --------- Data and styling goes here -------------- //



                package.Save();
        }



        }

        private class RefRow
        {
            public string intent;
            public string uri;
            public Dictionary<string, string> entities;
            public Dictionary<string, string> federations;

            public RefRow(string intent, string uri, Dictionary<string, string> matchedEntity, Dictionary<string, string> matchedFederation)
            {
                this.intent = intent;
                this.uri = uri;
                this.entities = matchedEntity;
                this.federations = matchedFederation;
            }
        }
    }
}
