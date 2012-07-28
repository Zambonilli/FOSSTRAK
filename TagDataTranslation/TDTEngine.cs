/*
 * Copyright (C) 2007-2012 University of Cambridge
 *
 * This file is part of Fosstrak (www.fosstrak.org).
 *
 * Fosstrak is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License version 2.1, as published by the Free Software Foundation.
 *
 * Fosstrak is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with Fosstrak; if not, write to the Free
 * Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
 * Boston, MA  02110-1301  USA
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FOSSTRAK.TDT
{
    /// <summary>
    ///  This class provides methods for translating an electronic product code (EPC) between various levels of representation 
    ///  including BINARY, TAG_ENCODING, PURE_IDENTITY and LEGACY formats. An additional output level ONS_HOSTNAME may be 
    ///  defined for some coding schemes.
    /// </summary>
    /// <remarks>Author Mike Lohmeier myname@gmail.com</remarks>
    public class TDTEngine
    {

        #region Data

        /// <summary>
        /// String formatter for a regex line
        /// </summary>
        private const String c_REGEXLINEFORMATTER = "^{0}$";

        /// <summary>
        /// associative array for the xref between GS1 company prefix &amp; Company prefix
        /// </summary>
        private Dictionary<String, String> _gs1cpi;

        /// <summary>
        /// IEnumerable of all of the options flattened out with ptrs to the option&apos;s Level and Scheme
        /// </summary>
        private List<Tuple<Scheme, Level, Option>> _options;

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor for a new Tag Data Translation engine.
        /// </summary>
        /// <remarks>
        /// Constructor for a new Tag Data Translation engine. This constructor loads and parses the schemes included in the directory Resources\Schemes 
        /// The ManagerTranslation.xml file is loaded from a directory called Resources\Auxiliary. All schemes must have filenames ending in .xml
        /// </remarks>
        public TDTEngine()
        {
            // load the schmes
            LoadEpcTagDataTranslations();

            // load the xref into 
            LoadGEPC64Table();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Translates <paramref name="epcIdentifier"/> from one representation into another within the same coding scheme.
        /// [TDT 1.6 Version]
        /// </summary>
        /// <param name="epcIdentifier">The epcIdentifier to be converted.  This should be expressed as a string, in accordance with one of the grammars
        /// or patterns in the TDT markup files, i.e. a binary string consisting of characters '0' and '1', a URI (either tag-encoding or pure-identity formats),
        /// or a serialized identifier expressed as in Table 3</param>
        /// <param name="parameterList">This is a parameter string containing key value pairs, using the semicolon [';'] as delimiter between key=value pairs.
        /// <example>GTIN filter=3;companyprefixlength=7;tagLength=96</example>
        /// </param>
        /// <param name="outputFormat">The output format into which the epcIdentifier SHALL be converted.
        /// <value>BINARY</value>
        /// <value>LEGACY</value>
        /// <value>LEGACY_AI</value>
        /// <value>TAG_ENCODING</value>
        /// <value>PURE_IDENTITY</value>
        /// <value>ONS_HOSTNAME</value>
        /// </param>
        /// <returns>The converted value into one of the above formats as String.</returns>
        public String Translate(String epcIdentifier, String parameterList, String outputFormat)
        {
            // paramater normalization/validation
            epcIdentifier = epcIdentifier ?? String.Empty;
            epcIdentifier = epcIdentifier.Trim();
            parameterList = parameterList ?? String.Empty;
            parameterList = parameterList.Trim();
            outputFormat = outputFormat ?? String.Empty;
            outputFormat = outputFormat.Trim();
            LevelTypeList type;
            if (String.IsNullOrEmpty(epcIdentifier)) throw new ArgumentNullException("epcIdentifier");
            if (String.IsNullOrEmpty(outputFormat)) throw new ArgumentNullException("outputFormat");
            if (!Enum.TryParse<LevelTypeList>(outputFormat, out type)) throw new ArgumentException("Invalid outputFormat", "outputFormat");

            // convert the paramterList string to IEnumerable<Kvp<String, String>>
            List<KeyValuePair<String, String>> parameters = new List<KeyValuePair<string, string>>();
            String[] split = parameterList.Split(';');
            foreach (String s in split)
            {
                String[] split2 = s.Trim().Split('=');
                if (split2.Length == 2)
                {
                    parameters.Add(new KeyValuePair<string, string>(split2[0].Trim(), split2[1].Trim()));
                }
            }

            return Translate(epcIdentifier, parameters, type);
        }

        /// <summary>
        /// Translates <paramref name="epcIdentifier"/> from one representation into another within the same coding scheme. 
        /// [.NET Version]
        /// </summary>
        /// <param name="epcIdentifier">The epcIdentifier to be converted.  This should be expressed as a string, in accordance with one of the grammars
        /// or patterns in the TDT markup files, i.e. a binary string consisting of characters '0' and '1', a URI (either tag-encoding or pure-identity formats),
        /// or a serialized identifier expressed as in Table 3</param>
        /// <param name="parameterList">IEnumerable list of key value pair parameters needed for doing some translations</param>
        /// <param name="outputFormat">The output format for the <paramref name="epcIdentifier"/> in the same scheme</param>
        /// <returns>The converted value into one of the above formats as String.</returns>
        public String Translate(String epcIdentifier, IEnumerable<KeyValuePair<String, String>> parameterList, LevelTypeList outputFormat)
        {
            // input normalization/validation
            epcIdentifier = epcIdentifier ?? String.Empty;
            epcIdentifier = epcIdentifier.Trim();
            epcIdentifier = Uri.UnescapeDataString(epcIdentifier);
            parameterList = parameterList ?? default(IEnumerable<KeyValuePair<String, String>>);
            if (String.IsNullOrEmpty(epcIdentifier)) throw new ArgumentNullException("input");

            // determine the input Option
            Tuple<Scheme, Level, Option> inputOption = GetInputOption(epcIdentifier, parameterList);

            // determine the output Option, seems to easy of a query...
            Tuple<Scheme, Level, Option> outputOption = _options.Single((o) => o.Item2.type == outputFormat & o.Item3.optionKey == inputOption.Item3.optionKey);

            // extract the input tokens
            String[] fieldTokens = ExtractInputTokens(epcIdentifier, parameterList, inputOption, outputOption);

            // now derive some new tokens from basic fx & input tokens
            Dictionary<String, String> extractTokens = ProcessRules(inputOption, fieldTokens, ModeList.EXTRACT);



            return null;
        }

        /// <summary>
        /// Checks each subscription for any update, reloading new rules where necessary and forces the software to reload or recompile its internal representation
        /// of the encoding/decoding rules based on the current remaining subscriptions.
        /// </summary>
        public void RefreshTranslations()
        {
            // load the schmes
            LoadEpcTagDataTranslations();

            // load the xref into 
            LoadGEPC64Table();
        }

        #endregion


        /// <summary>
        /// Method that loads and parses the Resources\Auxilary\ManagerTranslation.xml into <see cref="_gs1cpi"/>
        /// </summary>
        /// <exception cref="System.Security.SecurityException">
        /// When the process user account doesn&apos;t have read permissions to the ManagerTranslation.xml file
        /// </exception>
        /// <exception cref="System.IO.FileNotFoundException">
        /// When the ManagerTranslation.xml file is not found
        /// </exception>
        private void LoadGEPC64Table()
        {
            _gs1cpi = new Dictionary<string, string>();
            XmlReader reader = XmlReader.Create(Environment.CurrentDirectory +
                @"\Resources\Auxiliary\ManagerTranslation.xml");
            while (reader.Read())
            {
                if (reader.Name == "entry")
                {
                    _gs1cpi.Add(reader.GetAttribute("index"),
                        reader.GetAttribute("companyPrefix"));
                }
            }
        }

        /// <summary>
        /// Method for loading all of the *.xml schemes files from disk to main memory data structures
        /// </summary>
        private void LoadEpcTagDataTranslations()
        {
            // reinit _options and read the file system for the scheme files
            this._options = new List<Tuple<Scheme, Level, Option>>();
            String[] fileNames = Directory.GetFiles(Environment.CurrentDirectory + @"\Resources\Schemes");

            // thread out the reading/deserialization
            Task[] tasks = new Task[fileNames.Length];
            for (int i = 0; i < fileNames.Length; i++)
            {
                tasks[i] = Task.Factory.StartNew(new Action<object>((fileName) =>
                    {
                        // deserialize the file into the xsd.exe classes
                        XmlSerializer serializer = new XmlSerializer(typeof(EpcTagDataTranslation));
                        EpcTagDataTranslation tdt = (EpcTagDataTranslation)serializer.Deserialize(new FileStream(((String)fileName), FileMode.Open));

                        // do a breadth first traversal to flatten out and add to the list
                        foreach (Scheme s in tdt.scheme)
                        {
                            foreach (Level l in s.level)
                            {
                                foreach (Option o in l.option)
                                {
                                    lock (_options)
                                    {
                                        _options.Add(new Tuple<Scheme, Level, Option>(s, l, o));
                                    }
                                }
                            }
                        }
                    }),
                    fileNames[i]);
            }
            Task.WaitAll(tasks);
        }

        /// <summary>
        /// Gets the Option for the input and the inputParameters
        /// </summary>
        /// <param name="epcIdentifier">The tag input</param>
        /// <param name="paramterList">Additional parameters for Converting</param>
        private Tuple<Scheme, Level, Option> GetInputOption(String epcIdentifier, IEnumerable<KeyValuePair<String, String>> paramterList)
        {
            // get the optional taglength param
            String temp = GetInputParameterValue("taglength", paramterList);
            int? tagLength = (String.IsNullOrEmpty(temp)) ? null : new int?(int.Parse(temp));

            // build an über query for finding the correct option
            var query = from option in _options
                        where ((!String.IsNullOrEmpty(option.Item2.prefixMatch)) && (epcIdentifier.StartsWith(option.Item2.prefixMatch))) &&
                              ((!tagLength.HasValue) || (int.Parse(option.Item1.tagLength) == tagLength.Value)) &
                              (new Regex(String.Format(c_REGEXLINEFORMATTER, option.Item3.pattern)).Match(epcIdentifier).Success) &
                              (((option.Item2.type != LevelTypeList.BINARY) & (option.Item2.type != LevelTypeList.PURE_IDENTITY) & (option.Item2.type != LevelTypeList.TAG_ENCODING)) &
                                (option.Item3.optionKey == GetInputParameterValue(option.Item1.optionKey, paramterList)))
                        select option;

            // process the results
            Tuple<Scheme, Level, Option>[] results = query.ToArray();
            if (results.Length == 0)
            {
                throw new TDTTranslationException("No matching Scheme, Level, and Option for the input & inputParameters");
            }
            else if (results.Length > 1)
            {
                throw new TDTTranslationException("Multiple matching Scheme, Level and Options for the input & inputParameters");
            }
            return results[0];
        }

        /// <summary>
        /// Method for deriving new tokens from the 
        /// </summary>
        /// <param name="fieldTokens">The extracted field tokens from the input</param>
        /// <param name="inputOption">The epcIdentifier&apos;s Scheme,Level and Option</param>
        /// <param name="ruleType">The type of rule to process</param>
        /// <returns>
        /// An associative array of the derived values
        /// </returns>
        private Dictionary<String, String> ProcessRules(Tuple<Scheme, Level, Option> inputOption, String[] fieldTokens, ModeList ruleType)
        {
            Dictionary<String, String> derivedTokens = new Dictionary<string, string>();
            foreach (Rule r in inputOption.Item2.rule)
            {
                if (r.type == ruleType)
                {
                    // parse the command name & it's parameters
                    Regex rx = new Regex(@"^(.+)\((.+)\)$");
                    Match m = rx.Match(r.function);
                    if ((m.Success) &
                        (m.Captures.Count == 2))
                    {
                        //TODO Switch to an AST via codedom 
                        String functionName = m.Captures[0].Value.ToLower().Trim();
                        String[] functionParameters = m.Captures[1].Value.Split(',');
                        String seq1 = inputOption.Item3.field.Single(f => f.name == functionParameters[0]).seq;
                        String sequence1Value = fieldTokens[int.Parse(seq1)];
                        switch (functionName)
                        {
                            case "tablelookup":
                                {
                                    // EX: TABLELOOKUP(gs1companyprefixindex,tdt64bitcpi,gs1companyprefixindex,gs1companyprefix)
                                    if (functionParameters[1].Trim().ToLower() == "tdt64bitcpi")
                                    {
                                        derivedTokens.Add(r.newFieldName, _gs1cpi[sequence1Value]);
                                    }
                                    else
                                    {
                                        throw new TDTTranslationException("TDTFileNotFound " + functionParameters[1] + " auxillary file not found");
                                    }
                                    break;
                                }
                            case "length":
                                {
                                    derivedTokens.Add(r.newFieldName, sequence1Value.Length.ToString());
                                    break;
                                }
                            case "gs1checksum":
                                {
                                    int checksum;
                                    int weight;
                                    int total = 0;
                                    int len = sequence1Value.Length;
                                    int d;
                                    for (int i = 0; i < len; i++)
                                    {
                                        if (i % 2 == 0)
                                        {
                                            weight = -3;
                                        }
                                        else
                                        {
                                            weight = -1;
                                        }
                                        d = int.Parse(sequence1Value.Substring(len - 1 - i, len - i));
                                        total += weight * d;
                                    }
                                    checksum = (10 + total % 10) % 10;
                                    derivedTokens.Add(r.newFieldName, checksum.ToString());
                                    break;
                                }
                            case "substr":
                                {
                                    if (functionParameters.Length == 2)
                                    {
                                        derivedTokens.Add(r.newFieldName, sequence1Value.Substring(int.Parse(functionParameters[1])));
                                    }
                                    else if (functionParameters.Length == 3)
                                    {
                                        derivedTokens.Add(r.newFieldName, sequence1Value.Substring(int.Parse(functionParameters[1]), int.Parse(functionParameters[2])));
                                    }
                                    break;
                                }
                            case "concat":
                                {
                                    StringBuilder buffer = new StringBuilder();
                                    for (int p1 = 0; p1 < functionParameters.Length; p1++)
                                    {
                                        Match m2 = new Regex(("\"(.*?)\"|'(.*?)'|[0-9]")).Match(fieldTokens[p1]);
                                        if (m2.Success)
                                        {
                                            buffer.Append(functionParameters[p1]);
                                        }
                                        else
                                        {
                                            if (derivedTokens.ContainsKey(functionParameters[p1]))
                                            {
                                                buffer.Append(derivedTokens[functionParameters[p1]]);
                                            }
                                            String temp = fieldTokens.SingleOrDefault(t => t == functionParameters[p1]);
                                            if (temp != null)
                                            {
                                                buffer.Append(temp);
                                            }
                                        }
                                    }
                                    derivedTokens.Add(r.newFieldName, buffer.ToString());
                                    break;
                                }
                            case "add":
                                {
                                    int value1 = int.Parse(sequence1Value);
                                    int value2 = int.Parse(functionParameters[1]);
                                    derivedTokens.Add(r.newFieldName, (value1 + value2).ToString());
                                    break;
                                }
                            case "multiply":
                                {
                                    int value1 = int.Parse(sequence1Value);
                                    int value2 = int.Parse(functionParameters[1]);
                                    derivedTokens.Add(r.newFieldName, (value1 * value2).ToString());
                                    break;
                                }
                            case "divide":
                                {
                                    int value1 = int.Parse(sequence1Value);
                                    int value2 = int.Parse(functionParameters[1]);
                                    derivedTokens.Add(r.newFieldName, (value1 / value2).ToString());
                                    break;
                                }
                            case "subtract":
                                {
                                    int value1 = int.Parse(sequence1Value);
                                    int value2 = int.Parse(functionParameters[1]);
                                    derivedTokens.Add(r.newFieldName, (value1 - value2).ToString());
                                    break;
                                }
                            case "mod":
                                {
                                    int value1 = int.Parse(sequence1Value);
                                    int value2 = int.Parse(functionParameters[1]);
                                    derivedTokens.Add(r.newFieldName, (value1 % value2).ToString());
                                    break;
                                }
                        }
                    }
                }
            }
            return derivedTokens;
        }

        /// <summary>
        /// Method to extract, validate and format the field tokens from the input into its string representation
        /// </summary>
        /// <param name="epcIdentifier">The input string to extract the tokens from</param>
        /// <param name="parameterList">IEnumerable of kvp parameters for doing the translation</param>
        /// <param name="option">The <paramref name="epcIdentifier"/>&apos;s option</param>
        /// <returns>An array of the extracted tokens from the <paramref name="epcIdentifier"/></returns>
        private String[] ExtractInputTokens(String epcIdentifier, IEnumerable<KeyValuePair<String, String>> parameterList, Tuple<Scheme, Level, Option> inputOption, Tuple<Scheme, Level, Option> outputOption)
        {
            // now extract the various fields for the option from the input
            Match m = new Regex(String.Format(c_REGEXLINEFORMATTER, inputOption.Item3.pattern)).Match(epcIdentifier);
            String[] fields = new String[inputOption.Item3.field.Length];
            Field f;
            String token;
            for (int i = 0; i < inputOption.Item3.field.Length; i++)
            {
                f = inputOption.Item3.field[i];
                token = m.Groups[int.Parse(f.seq)].Value;

                // check if we have to uncompact & convert the binary into a decimal
                if (inputOption.Item2.type == LevelTypeList.BINARY)
                {
                    // check if it is compacted
                    if (f.compactionSpecified)
                    {
                        int? compactNumber = null;
                        switch (f.compaction)
                        {
                            case CompactionMethodList.Item5bit:
                                compactNumber = new int?(5);
                                break;
                            case CompactionMethodList.Item6bit:
                                compactNumber = new int?(6);
                                break;
                            case CompactionMethodList.Item7bit:
                                compactNumber = new int?(7);
                                break;
                            case CompactionMethodList.Item8bit:
                                compactNumber = new int?(8);
                                break;
                        }
                        if ((f.bitPadDirSpecified) &
                            (compactNumber.HasValue))
                        {
                            // strip the preceding or trailing 0 chars based on the compaction bit level
                            token = StripTokenBinaryPadding(token, f.bitPadDir, compactNumber.Value);
                        }

                        // convert the sequence of bytes to a string
                        token = BinaryToString(token, compactNumber.Value);

                        // check the character set
                        CheckTokenCharacterSet(f, token);
                    }
                    else
                    {
                        if (f.bitPadDirSpecified)
                        {
                            token = StripTokenBinaryPadding(token, f.bitPadDir, 0);
                        }

                        // convert the sequence of bytes to a string
                        token = Bin2Dec(token);

                        if (token.Length > 0)
                        {
                            CheckTokenMinMax(f, token);
                        }
                    }

                    Field outputField = outputOption.Item3.field.Single((f2) => f2.name == f.name);
                    if (f.padDirSpecified)
                    {
                        if (outputField.padDirSpecified)
                        {
                            throw new TDTTranslationException("Invalid TDT definition file");
                        }
                        else
                        {
                            token = StripPadChar(token, f.padDir, f.padChar);
                        }
                    }
                    else
                    {
                        if (outputField.padDirSpecified)
                        {
                            token = ApplyPadChar(token, outputField.padDir, outputField.padChar, int.Parse(outputField.length));
                        }
                    }
                }
                else
                {
                    // check the character set
                    CheckTokenCharacterSet(f, token);

                    // check the min/max
                    CheckTokenMinMax(f, token);
                }

                // add the extracted, validated & formated token to the return array
                fields[i] = token;
            }
            return fields;
        }

        /// <summary>
        /// Method to check the CharacterSet of an input token
        /// </summary>
        /// <param name="f"></param>
        /// <param name="token"></param>
        private static void CheckTokenCharacterSet(Field f, String token)
        {
            if (!String.IsNullOrEmpty(f.characterSet))
            {
                String pattern = (f.characterSet.EndsWith("*")) ? f.characterSet : f.characterSet += "*";
                if (!new Regex(String.Format(c_REGEXLINEFORMATTER, pattern)).IsMatch(token))
                {
                    throw new TDTTranslationException("Invalid " + f.name + " field value " + token + " according to its character set " + f.characterSet);
                }
            }
        }

        /// <summary>
        /// Helper to find an input parameter if it exists in the <paramref name="inputParameters"/>
        /// </summary>
        /// <param name="parameterName">The name of the parameter</param>
        /// <param name="inputParameters">The collection of inputParameters</param>
        /// <returns>
        /// <para>The value of the input parameter</para>
        /// <para>null</para>
        /// </returns>
        private static String GetInputParameterValue(String parameterName, IEnumerable<KeyValuePair<String, String>> inputParameters)
        {
            if (String.IsNullOrEmpty(parameterName)) throw new ArgumentNullException("parameterName");
            if (inputParameters == null) throw new ArgumentNullException("inputParameters");

            KeyValuePair<String, String> kvp = inputParameters.SingleOrDefault((kvp2) => kvp2.Key.ToLower().Trim() == parameterName.Trim().ToLower());
            if (!kvp.Equals(default(KeyValuePair<String, String>)))
            {
                return kvp.Value;
            }
            return null;
        }

        /// <summary>
        /// Method to check an input tokens min and max based on the min and max field attributes
        /// </summary>
        /// <param name="fieldName">The name of the field</param>
        /// <param name="token"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        private static void CheckTokenMinMax(Field f, String token)
        {
            Decimal result, min, max;
            if ((!String.IsNullOrEmpty(f.decimalMinimum)) &&
                (!String.IsNullOrEmpty(f.decimalMaximum)) &&
                (!String.IsNullOrEmpty(token)) &&
                (Decimal.TryParse(token, out result)) &
                (Decimal.TryParse(f.decimalMinimum, out min)) &
                (Decimal.TryParse(f.decimalMaximum, out max)))
            {
                if (result < min)
                {
                    throw new TDTTranslationException("TDTFieldBelowMinimum Field:" + f.name + " Value:" + token + " Min:" + min.ToString());
                }
                if (result > max)
                {
                    throw new TDTTranslationException("TDTFieldAboveMaximum Field:" + f.name + " Value:" + token + " Max:" + max.ToString());
                }
            }
        }

        /// <summary>
        /// Method to Strip the binary padding from a token
        /// </summary>
        /// <param name="bitPadDir">The bit side that has been compacted</param>
        /// <param name="compaction">The compaction base ie 8bit, 7bit</param>
        /// <param name="token">Token to strip binary padding from</param>
        /// <returns><paramref name="token"/> stripped of all padding</returns>
        private static String StripTokenBinaryPadding(String token, PadDirectionList bitPadDir, int compaction)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation            
            if (compaction >= 4)
            {
                if (bitPadDir == PadDirectionList.RIGHT)
                {
                    int lastnonzerobit = token.LastIndexOf("1");
                    int bitsforstripped = compaction * (1 + lastnonzerobit / compaction);
                    return token.Substring(0, bitsforstripped);
                }
                else
                {
                    int firstnonzerobit = token.IndexOf("1");
                    int length = token.Length;
                    int bitsforstripped = compaction * (1 + (length - firstnonzerobit) / compaction);
                    return token.Substring(length - bitsforstripped);
                }

            }
            else
            {
                if (bitPadDir == PadDirectionList.RIGHT)
                {
                    int lastnonzerobit = token.LastIndexOf("1");
                    return token.Substring(0, lastnonzerobit);
                }
                else
                {
                    int firstnonzerobit = token.IndexOf("1");
                    return token.Substring(firstnonzerobit);
                }

            }
        }

        /// <summary>
        /// Converts a binary string to a character string according to the specified compaction
        /// </summary>
        /// <param name="value"></param>
        /// <param name="compaction"></param>
        /// <returns></returns>
        private String BinaryToString(String value, int compaction)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            switch (compaction)
            {
                case 5:
                    return Bin2UpperCaseFive(value);
                case 6:
                    return Bin2AlphaNumSix(value);
                case 7:
                    return bin2asciiseven(value);
                case 8:
                    return bin2bytestring(value);
                // future unicode support goes here
                default:
                    throw new TDTTranslationException("unsupported compaction method " + compaction.ToString());
            }
        }

        private String bin2bytestring(String binary)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            String bytestring;
            StringBuilder buffer = new StringBuilder();
            int len = binary.Length;
            for (int i = 0; i < len; i += 8)
            {
                int j = int.Parse(Bin2Dec(padBinary(binary.Substring(i, i + 8), 8)));
                buffer.Append((char)j);
            }
            bytestring = buffer.ToString();
            return bytestring;
        }

        private String bin2asciiseven(String binary)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            String asciiseven;
            StringBuilder buffer = new StringBuilder();
            int len = binary.Length;
            for (int i = 0; i < len; i += 7)
            {
                int j = int.Parse(Bin2Dec(padBinary(binary.Substring(i, i + 7), 8)));
                buffer.Append((char)j);
            }
            asciiseven = buffer.ToString();
            return asciiseven;
        }

        private String Bin2AlphaNumSix(String binary)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            String alphanumsix;
            StringBuilder buffer = new StringBuilder("");
            int len = binary.Length;
            for (int i = 0; i < len; i += 6)
            {
                int j = int.Parse(Bin2Dec(padBinary(binary.Substring(i, i + 6), 8)));
                if (j < 32)
                {
                    j += 64;
                }
                buffer.Append((char)j);
            }
            alphanumsix = buffer.ToString();
            return alphanumsix;
        }

        private String Bin2UpperCaseFive(String binary)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            String uppercasefive;
            StringBuilder buffer = new StringBuilder();
            int len = binary.Length;
            for (int i = 0; i < len; i += 5)
            {
                int j = int.Parse(Bin2Dec(padBinary(binary.Substring(i, i + 5), 8)));
                buffer.Append((char)(j + 64));
            }
            uppercasefive = buffer.ToString();
            return uppercasefive;
        }


        public String Bin2Dec(String binary)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            if (binary.Length == 0)
            {
                return "0";
            }
            else
            {
                long dec = long.Parse(binary);
                return dec.ToString();
            }
        }

        private String padBinary(String binary, int reqlen)
        {
            // TODO Buy ISO 15962 and figure out how single char compaction works 
            // with RFID this is just a logical port of the java 1.4 FOSSTRAK implementation
            String rv;
            int l = binary.Length;
            int pad = (reqlen - (l % reqlen)) % reqlen; // (8 - (1 % 5)) % 8
            StringBuilder buffer = new StringBuilder();
            for (int i = 0; i < pad; i++)
            {
                buffer.Append("0");
            }
            buffer.Append(binary);
            rv = buffer.ToString();
            return rv;
        }

        private String StripPadChar(String padded, PadDirectionList dir, String padchar)
        {
            if (dir == PadDirectionList.LEFT)
            {
                return padded.Substring(padded.IndexOf(padchar));
            }
            else
            {
                return padded.Substring(0, padded.Length - padded.LastIndexOf(padchar));
            }
        }

        private String ApplyPadChar(String bare, PadDirectionList dir, String padchar, int requiredLength)
        {
            if (dir == null || padchar == null || requiredLength == -1)
            {
                return bare;
            }
            else
            {
                StringBuilder buf = new StringBuilder(requiredLength);
                for (int i = 0; i < requiredLength - bare.Length; i++)
                {
                    buf.Append(padchar);
                }

                if (dir == PadDirectionList.RIGHT)
                    return bare + buf.ToString();
                else
                    // if (dir == PadDirectionList.LEFT)
                    return buf.ToString() + bare;
            }
        }
    }
}
