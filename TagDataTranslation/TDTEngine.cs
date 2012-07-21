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
    ///  This class provides methods for translating an electronic product code (EPC)
    ///  between various levels of representation including BINARY, TAG_ENCODING,
    ///  PURE_IDENTITY and LEGACY formats. An additional output level ONS_HOSTNAME may
    ///  be defined for some coding schemes.
    /// </summary>
    /// <remarks>Author Mike Lohmeier myname@gmail.com</remarks>
    public class TDTEngine
    {

        #region Class/Member Variables

        /// <summary>
        /// associative array for the xref between GS1 company prefix &amp; Company prefix
        /// </summary>
        private Dictionary<String, String> _gs1cpi = new Dictionary<string, string>();

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
        /// The ManagerTranslation.xml file is loaded from a directory
        /// called Resources\Auxiliary. All schemes must have filenames ending in .xml
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
        /// The convert method translates a String input to a specified outbound
        /// level of the same coding scheme. For example, the input string value may
        /// be a tag-encoding URI and the outbound level specified by string
        /// outboundlevel may be BINARY, in which case the return value is a binary
        /// representation expressed as a string.
        /// </summary>
        /// <param name="input">the identifier to be converted.</param>
        /// <param name="suppliedInputParameters">additional parameters which need to be provided because they cannot always be determined from the input value alone. Examples include the taglength, companyprefixlength, gs1companyprefixlength and filter values.</param>
        /// <param name="outputLevel">the outbound level required for the ouput. Permitted values include BINARY, TAG_ENCODING, PURE_IDENTITY, LEGACY and ONS_HOSTNAME.</param>
        /// <returns>the identifier converted to the output level.</returns>
        public String Convert(String input, IEnumerable<KeyValuePair<String, String>> inputParameters, LevelTypeList outputLevel)
        {
            // input validation
            if (String.IsNullOrEmpty(input)) throw new ArgumentNullException("input");
            if (inputParameters == null) throw new ArgumentNullException("inputParameters");

            // escape any uri chars that maybe encoded
            input = Uri.UnescapeDataString(input);

            // determine the input Option
            Tuple<Scheme, Level, Option> option = GetInputOption(input, inputParameters);

            return null;
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
        /// <param name="input">The tag input</param>
        /// <param name="inputParameters">Additional parameters for Converting</param>
        private Tuple<Scheme, Level, Option> GetInputOption(String input, IEnumerable<KeyValuePair<String, String>> inputParameters)
        {
            // get the optional taglength param
            String temp = GetInputParameterValue("taglength", inputParameters);
            int? tagLength = (String.IsNullOrEmpty(temp)) ? null : new int?(int.Parse(temp));

            // build an über query for finding the correct option
            var query = from option in _options
                        where ((!String.IsNullOrEmpty(option.Item2.prefixMatch)) && (input.StartsWith(option.Item2.prefixMatch))) &&
                              ((!tagLength.HasValue) || (int.Parse(option.Item1.tagLength) == tagLength.Value)) &
                              (new Regex("^" + option.Item3.pattern + "$").Match(input).Success) &
                              (((option.Item2.type != LevelTypeList.BINARY) & (option.Item2.type != LevelTypeList.PURE_IDENTITY) & (option.Item2.type != LevelTypeList.TAG_ENCODING)) &
                                (option.Item3.optionKey == GetInputParameterValue(option.Item1.optionKey, inputParameters)))
                        select option;

            Tuple<Scheme, Level, Option>[] results = query.ToArray();

            if (results.Length == 0)
            {
                throw new TDTException("No matching Scheme, Level, and Option for the input & inputParameters");
            }
            else if (results.Length > 1)
            {
                throw new TDTException("Multiple matching Scheme, Level and Options for the input & inputParameters");
            }
            return results[0];
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
            if (!kvp.Equals(default(KeyValuePair<String,String>)))
            {
                return kvp.Value;
            }
            return null;
        }
    }
}
