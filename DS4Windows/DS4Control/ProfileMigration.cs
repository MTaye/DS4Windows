﻿/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Xml;
using static DS4Windows.StickDeadZoneInfo;

namespace DS4Windows
{
    public class ProfileMigration
    {
        private XmlReader profileReader;
        public XmlReader ProfileReader { get => profileReader; }

        private int configFileVersion;
        private bool usedMigration;
        public bool UsedMigration { get => usedMigration; }
        private string currentMigrationText;
        public string CurrentMigrationText { get => currentMigrationText; }

        public ProfileMigration(Stream inputStream)
        {
            //FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            SetupFromStream(inputStream);
            inputStream.Close();
        }

        public ProfileMigration(string profileText)
        {
            using (MemoryStream tempMemStream =
                new MemoryStream(Encoding.UTF8.GetBytes(profileText ?? "")))
            {
                SetupFromStream(tempMemStream);
            }
        }

        private void SetupFromStream(Stream inputStream)
        {
            StreamReader innerStreamReader = new StreamReader(inputStream);
            currentMigrationText = innerStreamReader.ReadToEnd();
            innerStreamReader.Dispose();

            bool profileRead = false;
            try
            {
                profileReader = XmlReader.Create(new StringReader(currentMigrationText));
                // Move stream to root element
                profileReader.MoveToContent();
                string temp = profileReader.GetAttribute("config_version");
                if (!string.IsNullOrEmpty(temp))
                {
                    int.TryParse(temp, out configFileVersion);
                }

                profileRead = true;
            }
            catch (XmlException) { }

            // config_version not available in file. Assume either version 1 or 2.
            // Try to determine which version
            if (profileRead && configFileVersion == 0)
            {
                DetermineProfileVersion();
            }
        }

        public bool RequiresMigration()
        {
            bool result = false;
            if (configFileVersion >= 1 && configFileVersion < Global.CONFIG_VERSION)
            {
                result = true;
            }

            return result;
        }

        public void Migrate()
        {
            if (RequiresMigration())
            {
                string migratedText;
                int tempVersion = configFileVersion;
                switch (configFileVersion)
                {
                    case 1:
                        migratedText = Version0002Migration();
                        currentMigrationText = migratedText;
                        PrepareReaderMigration(migratedText);
                        tempVersion = 2;
                        goto case 2;
                    case 2:
                    case 3:
                        migratedText = Version0004Migration();
                        currentMigrationText = migratedText;
                        PrepareReaderMigration(migratedText);
                        tempVersion = 4;
                        goto case 4;
                    case 4:
                        migratedText = Version0005Migration();
                        currentMigrationText = migratedText;
                        PrepareReaderMigration(migratedText);
                        tempVersion = 5;
                        goto case 5;
                    case 5:
                        migratedText = Version0006Migration();
                        currentMigrationText = migratedText;
                        PrepareReaderMigration(migratedText);
                        tempVersion = 6;
                        goto default;
                    default:
                        break;
                }

                configFileVersion = tempVersion;
            }
        }

        private void PrepareReaderMigration(string migratedText)
        {
            usedMigration = true;
            // Close and flush current XmlReader instance
            profileReader.Close();
            profileReader.Dispose();

            currentMigrationText = migratedText;
            StringReader stringReader = new StringReader(currentMigrationText);
            profileReader = XmlReader.Create(stringReader);
            // Move stream to root element
            //profileReader.MoveToContent();
        }

        public void Close()
        {
            if (profileReader != null)
            {
                // Close and flush current XmlReader instance
                profileReader.Close();
                profileReader.Dispose();

                profileReader = null;
            }
        }

        private void DetermineProfileVersion()
        {
            bool hasAntiDeadLSTag = false;
            //int deadZoneLS = -1;

            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            while (profileReader.Read())
            {
                /*if (profileReader.Name == "LSDeadZone" && profileReader.IsStartElement())
                {
                    string weight = profileReader.ReadElementContentAsString();
                    int.TryParse(weight, out deadZoneLS);
                }
                */
                if (profileReader.Name == "LSAntiDeadZone" && profileReader.IsStartElement())
                {
                    hasAntiDeadLSTag = true;
                    profileReader.ReadElementContentAsString();
                }
            }

            // Close and dispose current XmlReader
            profileReader.Close();
            profileReader.Dispose();

            if (hasAntiDeadLSTag)
            {
                configFileVersion = 2;
            }
            else
            {
                configFileVersion = 1;
            }

            // Start reader at zero position
            profileReader = XmlReader.Create(new StringReader(currentMigrationText));
            // Move stream to root element
            profileReader.MoveToContent();
        }

        private string Version0002Migration()
        {
            StringWriter stringWrite = new StringWriter();
            XmlWriter tempWriter = XmlWriter.Create(stringWrite, new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            });
            tempWriter.WriteStartDocument();
            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            profileReader.MoveToContent();

            // Write replacement root element in XmlWriter
            tempWriter.WriteStartElement("DS4Windows");
            tempWriter.WriteAttributeString("app_version", Global.exeversion);
            tempWriter.WriteAttributeString("config_version", "2");

            while (!profileReader.EOF)
            {
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "LSDeadZone":
                            {
                                string lsdead = profileReader.ReadElementContentAsString();
                                bool valid = int.TryParse(lsdead, out int temp);
                                if (valid)
                                {
                                    // Add default deadzone if a 0 dead zone was set in profile.
                                    // Jays2Kings used implicit dead zones in the mapper code
                                    if (temp <= 0)
                                    {
                                        temp = StickDeadZoneInfo.DEFAULT_DEADZONE;
                                    }

                                    tempWriter.WriteElementString("LSDeadZone", temp.ToString());
                                }

                                break;
                            }
                        case "RSDeadZone":
                            {
                                string rsdead = profileReader.ReadElementContentAsString();
                                bool valid = int.TryParse(rsdead, out int temp);
                                if (valid)
                                {
                                    // Add default deadzone if a 0 dead zone was set in profile.
                                    // Jays2Kings used implicit dead zones in the mapper code
                                    if (temp <= 0)
                                    {
                                        temp = StickDeadZoneInfo.DEFAULT_DEADZONE;
                                    }

                                    tempWriter.WriteElementString("RSDeadZone", temp.ToString());
                                }

                                break;
                            }

                        default:
                            tempWriter.WriteNode(profileReader, true);
                            break;
                    }
                }
                else
                {
                    profileReader.Read();
                }
            }

            // End XML document and flush IO stream
            tempWriter.WriteEndElement();
            tempWriter.WriteEndDocument();
            tempWriter.Close();
            return stringWrite.ToString();
        }

        struct MigrationSettings0004
        {
            public const double DEFAULT_SMOOTH_WEIGHT = 0.5;

            public bool hasGyroMouseSmoothing;
            public bool hasGyroMouseSmoothingWeight;

            public bool useGyroMouseSmoothing;
            public double gyroMouseSmoothingWeight;

            public bool hasGyroMouseStickSmoothing;
            public bool hasGyroMouseStickSmoothingWeight;

            public bool useGyroMouseStickSmoothing;
            public double gyroMouseStickSmoothingWeight;
        }

        private string Version0004Migration()
        {
            MigrationSettings0004 gyroSmoothSettings = new MigrationSettings0004()
            {
                gyroMouseSmoothingWeight = MigrationSettings0004.DEFAULT_SMOOTH_WEIGHT,
                gyroMouseStickSmoothingWeight = MigrationSettings0004.DEFAULT_SMOOTH_WEIGHT,
            };

            void MigrateGyroMouseSmoothingSettings(XmlWriter xmlWriter)
            {
                // <GyroMouseSmoothingSettings>
                xmlWriter.WriteStartElement("GyroMouseSmoothingSettings");

                xmlWriter.WriteStartElement("UseSmoothing");
                xmlWriter.WriteValue(gyroSmoothSettings.useGyroMouseSmoothing.ToString());
                xmlWriter.WriteEndElement();

                if (gyroSmoothSettings.useGyroMouseSmoothing)
                {
                    xmlWriter.WriteStartElement("SmoothingMethod");
                    xmlWriter.WriteValue("weighted-average");
                    xmlWriter.WriteEndElement();
                }

                xmlWriter.WriteStartElement("SmoothingWeight");
                xmlWriter.WriteValue(gyroSmoothSettings.gyroMouseSmoothingWeight.ToString());
                xmlWriter.WriteEndElement();

                // </GyroMouseSmoothingSettings>
                xmlWriter.WriteEndElement();
            }

            void MigrateGyroMouseStickSmoothingSettings(XmlWriter xmlWriter)
            {
                // <GyroMouseStickSmoothingSettings>
                xmlWriter.WriteStartElement("GyroMouseStickSmoothingSettings");

                xmlWriter.WriteStartElement("UseSmoothing");
                xmlWriter.WriteValue(gyroSmoothSettings.useGyroMouseStickSmoothing.ToString());
                xmlWriter.WriteEndElement();

                if (gyroSmoothSettings.useGyroMouseStickSmoothing)
                {
                    xmlWriter.WriteStartElement("SmoothingMethod");
                    xmlWriter.WriteValue("weighted-average");
                    xmlWriter.WriteEndElement();
                }

                xmlWriter.WriteStartElement("SmoothingWeight");
                xmlWriter.WriteValue(gyroSmoothSettings.gyroMouseStickSmoothingWeight.ToString());
                xmlWriter.WriteEndElement();

                // </GyroMouseStickSmoothingSettings>
                xmlWriter.WriteEndElement();
            }

            StringWriter stringWrite = new StringWriter();
            XmlWriter tempWriter = XmlWriter.Create(stringWrite, new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            });
            tempWriter.WriteStartDocument();
            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            profileReader.MoveToContent();

            // Write replacement root element in XmlWriter
            tempWriter.WriteStartElement("DS4Windows");
            tempWriter.WriteAttributeString("app_version", Global.exeversion);
            tempWriter.WriteAttributeString("config_version", "4");

            // First pass
            while (!profileReader.EOF)
            {
                bool readNext = true;
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "GyroSmoothing":
                            {
                                gyroSmoothSettings.hasGyroMouseSmoothing = true;
                                string useSmooth = profileReader.ReadElementContentAsString();
                                bool.TryParse(useSmooth, out gyroSmoothSettings.useGyroMouseSmoothing);
                                readNext = false;
                                break;
                            }
                        case "GyroSmoothingWeight":
                            {
                                gyroSmoothSettings.hasGyroMouseSmoothingWeight = true;
                                string weight = profileReader.ReadElementContentAsString();
                                double.TryParse(weight, out gyroSmoothSettings.gyroMouseSmoothingWeight);
                                readNext = false;
                                break;
                            }
                        case "GyroMouseStickSmoothing":
                            {
                                gyroSmoothSettings.hasGyroMouseStickSmoothing = true;
                                string useSmooth = profileReader.ReadElementContentAsString();
                                bool.TryParse(useSmooth, out gyroSmoothSettings.useGyroMouseStickSmoothing);
                                readNext = false;
                                break;
                            }
                        case "GyroMouseStickSmoothingWeight":
                            {
                                gyroSmoothSettings.hasGyroMouseStickSmoothingWeight = true;
                                string weight = profileReader.ReadElementContentAsString();
                                double.TryParse(weight, out gyroSmoothSettings.gyroMouseStickSmoothingWeight);
                                readNext = false;
                                break;
                            }
                        default:
                            break;
                    }
                }

                if (readNext)
                {
                    profileReader.Read();
                }
            }

            // Close and dispose current XmlReader
            profileReader.Close();
            profileReader.Dispose();

            // Prepare for second pass
            StringReader stringReader = new StringReader(currentMigrationText);
            profileReader = XmlReader.Create(stringReader);
            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            profileReader.MoveToContent();

            // Second pass
            while (!profileReader.EOF)
            {
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "GyroSmoothing":
                            {
                                // Place new GyroMouseSmoothingSettings group where GyroSmoothing used to be
                                MigrateGyroMouseSmoothingSettings(tempWriter);
                                // Consume reset of element
                                profileReader.ReadElementContentAsString();
                                break;
                            }
                        case "GyroSmoothingWeight":
                            {
                                // Consume reset of element
                                profileReader.ReadElementContentAsString();
                                break;
                            }
                        case "GyroMouseStickSmoothing":
                            {
                                // Place new GyroMouseStickSmoothingSettings group where GyroSmoothing used to be
                                MigrateGyroMouseStickSmoothingSettings(tempWriter);
                                // Consume reset of element
                                profileReader.ReadElementContentAsString();
                                break;
                            }
                        case "GyroMouseStickSmoothingWeight":
                            {
                                // Consume reset of element
                                profileReader.ReadElementContentAsString();
                                break;
                            }
                        default:
                            tempWriter.WriteNode(profileReader, true);
                            break;
                    }
                }
                else
                {
                    profileReader.Read();
                }
            }

            // End XML document and flush IO stream
            tempWriter.WriteEndElement();
            tempWriter.WriteEndDocument();
            tempWriter.Close();
            return stringWrite.ToString();
        }

        private string Version0005Migration()
        {
            StringWriter stringWrite = new StringWriter();
            XmlWriter tempWriter = XmlWriter.Create(stringWrite, new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            });
            tempWriter.WriteStartDocument();
            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            profileReader.MoveToContent();

            // Write replacement root element in XmlWriter
            tempWriter.WriteStartElement("DS4Windows");
            tempWriter.WriteAttributeString("app_version", Global.exeversion);
            tempWriter.WriteAttributeString("config_version", "5");

            while (!profileReader.EOF)
            {
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "UseTPforControls":
                            {
                                string tpControls = profileReader.ReadElementContentAsString();
                                bool valid = bool.TryParse(tpControls, out bool temp);
                                if (valid && temp)
                                {
                                    tempWriter.WriteElementString("TouchpadOutputMode", TouchpadOutMode.Controls.ToString());
                                }

                                break;
                            }
                        default:
                            tempWriter.WriteNode(profileReader, true);
                            break;
                    }
                }
                else
                {
                    profileReader.Read();
                }
            }

            // End XML document and flush IO stream
            tempWriter.WriteEndElement();
            tempWriter.WriteEndDocument();
            tempWriter.Close();
            return stringWrite.ToString();
        }

        private string Version0006Migration()
        {
            StringWriter stringWrite = new StringWriter();
            XmlWriter tempWriter = XmlWriter.Create(stringWrite, new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            });
            tempWriter.WriteStartDocument();
            // Move stream to root element
            profileReader.MoveToContent();
            // Skip past root element
            profileReader.Read();
            profileReader.MoveToContent();

            // Write replacement root element in XmlWriter
            tempWriter.WriteStartElement("DS4Windows");
            tempWriter.WriteAttributeString("app_version", Global.exeversion);
            tempWriter.WriteAttributeString("config_version", "6");

            string lsDeadZoneType = null;
            string rsDeadZoneType = null;

            // First pass: Collect DeadZoneType values and skip DeadZoneTypeRadial/Axial
            while (!profileReader.EOF)
            {
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "LSDeadZoneType":
                            lsDeadZoneType = profileReader.ReadElementContentAsString();
                            break;
                        case "RSDeadZoneType":
                            rsDeadZoneType = profileReader.ReadElementContentAsString();
                            break;
                        case "LSDeadZoneTypeRadial":
                        case "LSDeadZoneTypeAxial":
                            if (lsDeadZoneType != null)
                            {
                                profileReader.ReadElementContentAsString();
                            }
                            break;
                        case "RSDeadZoneTypeRadial":
                        case "RSDeadZoneTypeAxial":
                            if (rsDeadZoneType != null)
                            {
                                profileReader.ReadElementContentAsString();
                            }
                            break;
                        default:
                            profileReader.Read();
                            break;
                    }
                }
                else
                {
                    profileReader.Read();
                }
            }

            // Close and dispose current XmlReader
            profileReader.Close();
            profileReader.Dispose();

            // Prepare for second pass
            StringReader stringRead = new StringReader(currentMigrationText);
            profileReader = XmlReader.Create(stringRead);
            profileReader.MoveToContent();
            profileReader.Read();
            profileReader.MoveToContent();

            // Second pass: Write new XML, replacing LSDeadZoneType with new elements
            while (!profileReader.EOF)
            {
                if (profileReader.IsStartElement() && profileReader.Depth == 1)
                {
                    switch (profileReader.Name)
                    {
                        case "LSDeadZoneType":
                            tempWriter.WriteNode(profileReader, true);
                            if (lsDeadZoneType != null)
                            {
                                tempWriter.WriteElementString("LSDeadZoneTypeRadial", lsDeadZoneType == "Axial" ? "False" : "True");
                                tempWriter.WriteElementString("LSDeadZoneTypeAxial", lsDeadZoneType == "Axial" ? "True" : "False");
                            }
                            break;
                        case "RSDeadZoneType":
                            tempWriter.WriteNode(profileReader, true);
                            if (rsDeadZoneType != null)
                            {
                                tempWriter.WriteElementString("RSDeadZoneTypeRadial", rsDeadZoneType == "Axial" ? "False" : "True");
                                tempWriter.WriteElementString("RSDeadZoneTypeAxial", rsDeadZoneType == "Axial" ? "True" : "False");
                            }
                            break;
                        case "LSDeadZoneTypeRadial":
                        case "LSDeadZoneTypeAxial":
                        case "RSDeadZoneTypeRadial":
                        case "RSDeadZoneTypeAxial":
                            profileReader.ReadElementContentAsString();
                            break;
                        default:
                            tempWriter.WriteNode(profileReader, true);
                            break;
                    }
                }
                else
                {
                    profileReader.Read();
                }
            }

            // End XML document and flush IO stream
            tempWriter.WriteEndElement();
            tempWriter.WriteEndDocument();
            tempWriter.Close();
            return stringWrite.ToString();
        }
    }
}
