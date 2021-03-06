﻿using fasttext;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace preprocessingtool
{
    public class NLCToFasttextTrainingFile
    {
        static void Main(string[] args)
        {
            string csvFilePath = args[0];

            int splitTrainingSets = 1;
            if(!String.IsNullOrEmpty(args[1]))
            {
                splitTrainingSets = Int32.Parse(args[1]);
            }

            GenerateFasttextTrainingFileFromCsvTable(csvFilePath, splitTrainingSets);
        }

        private static string FASTTEXT_LABEL_PREFIX = "__label__";

        class LabelAndQuestion
        {
            public string Label { get; set; }
            public string Question { get; set; }
        }

        public static void GenerateFasttextTrainingFileFromCsvTable(string csvFilePath, int splitTrainingSets)
        {
            if (File.Exists(csvFilePath))
            {
                Console.WriteLine("Reading file : " + csvFilePath + " ...");
                int lineCount = 0;
                var questions = new List<LabelAndQuestion>();
                using (StreamReader sr = new StreamReader(csvFilePath, Encoding.UTF8))
                {
                    string line = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        int intentIndex = line.LastIndexOf(',');
                        if (intentIndex < 0) throw new Exception("invalid file format");
                        var labelAndQuestion = new LabelAndQuestion();
                        labelAndQuestion.Question = line.Substring(0, intentIndex);
                        labelAndQuestion.Label = line.Substring(intentIndex+1);
                        questions.Add(labelAndQuestion);
                    }
                }
                Shuffle(questions);

                int bucketQuestionsCount = questions.Count / splitTrainingSets;

                string csvFileName = Path.GetFileNameWithoutExtension(csvFilePath);
                string csvFileDirectory = Path.GetDirectoryName(csvFilePath);
                for (int trainingSetNumber = 1; trainingSetNumber <= splitTrainingSets; trainingSetNumber++)
                {
                    string trainingFilePath = csvFileDirectory + Path.DirectorySeparatorChar + csvFileName + trainingSetNumber + ".train";
                    string validationFilePath = csvFileDirectory + Path.DirectorySeparatorChar + csvFileName + trainingSetNumber + ".valid";
                    if(splitTrainingSets == 1)
                    {
                        trainingFilePath = csvFileDirectory + Path.DirectorySeparatorChar + csvFileName + ".delete";
                        validationFilePath = csvFileDirectory + Path.DirectorySeparatorChar + csvFileName + ".train";
                    }
                    using (StreamWriter trainsw = new StreamWriter(trainingFilePath, false, Encoding.UTF8))
                    {
                        using (StreamWriter validsw = new StreamWriter(validationFilePath, false, Encoding.UTF8))
                        {
                            for (int questionIndex = 0; questionIndex < questions.Count; questionIndex++)
                            {
                                var labelAndQuestion = questions[questionIndex];

                                StringBuilder sbQuestion = new StringBuilder();
                                sbQuestion.Append(FASTTEXT_LABEL_PREFIX);
                                sbQuestion.Append(labelAndQuestion.Label);
                                sbQuestion.Append(' ');
                                sbQuestion.Append(SentenceClassifier.PreprocessSentence(labelAndQuestion.Question));

                                bool writeToValidation = questionIndex >= (trainingSetNumber - 1) * bucketQuestionsCount && questionIndex < trainingSetNumber * bucketQuestionsCount;
                                if (!writeToValidation)
                                {
                                    trainsw.WriteLine(sbQuestion.ToString());
                                }
                                else
                                {
                                    validsw.WriteLine(sbQuestion.ToString());
                                }
                                lineCount++;
                            }
                        }
                    }
                    Console.WriteLine("OK - " + lineCount + " training samples written to " + trainingFilePath);
                }
            }
            else
            {
                Console.WriteLine("ERROR : File " + csvFilePath + " doesn't exist");
            }
        }

        

        private static Random rng = new Random();

        public static void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
