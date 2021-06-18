﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Net.Http;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using WatsonWebserver;
using SixLabors.ImageSharp.Drawing;

namespace basehub
{
    public partial class main : Form
    {
        #region setup

        HttpClient httpClient = new HttpClient();
        BaseHubMap map = new BaseHubMap();        
        SqLiteDataBase dataBase = new SqLiteDataBase();

        //file path of map data
        string dataDirectory;
        //API Key for Google Cloud Platform
        string apiKey;

        string _telemetryDatabaseName = "telemetry.db";
        string _mapDataFileName = "MapData.json";

        public main()
        {            
            InitializeComponent();
            WebServerSetup();
        }

        private void LoadIni(string path)
        {
            if (File.Exists(path))
            {
                //load configurations from ini.json
                JObject iniData = LoadJobjectFromFile(path);

                dataDirectory = iniData["dataDirectory"].ToString();
                apiKey = iniData["apiKey"].ToString();
            }
            else
            {
                //create new ini.json
                JObject iniData = new JObject();

                iniData.Add("dataDirectory", Microsoft.VisualBasic.Interaction.InputBox("Enter Data Directory", "Data Directory"));
                iniData.Add("apiKey", Microsoft.VisualBasic.Interaction.InputBox("Enter Google Cloud Platform API Key", "Api Key"));
                
                SaveJobjectToFile(iniData, path);
            }
        }
        #endregion

        #region httpCLient

        private bool HttpGetString(Uri uri, out string data)
        {
            try
            {
                HttpResponseMessage response = httpClient.GetAsync(uri).Result;
                response.EnsureSuccessStatusCode();
                data = response.Content.ReadAsStringAsync().Result;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                data = "";
                return false;
            }

        }

        private bool HttpGetStream(Uri uri, out Stream stream)
        {
            try
            {
                stream = httpClient.GetStreamAsync(uri).Result;
                return true;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                stream = null;
                return false;
            }            
        }

        #endregion

        #region mapData
        private int GetMapSize(int width, int height, int scale, out string size)
        {
            if (width > 640 || height > 640)
            {
                scale = GetMapSize(width / 2, height / 2, scale + 1, out size);
            }
            else
            {
                size = $"{width}x{height}";
            }
            //maximum available Scale of Google Maps Static API is 2 
            if (scale > 2)
            {
                scale = 2;
            }
            return scale;
        }

        private bool SaveMapData(BaseHubMap map, string path)
        {
            if (File.Exists(path))
            {
                //Load mapData from File
                JObject mapData = LoadJobjectFromFile(path);

                //add newMapData to mapData
                mapData.Add($"map_{mapData.Count}", map.GetJObject());

                //save mapData to File
                SaveJobjectToFile(mapData, path);

                return true;
            }
            else
            {
                //Check of the Directory to save to exists
                if (!Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                {
                    //Try to create the non existing Directory
                    try
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    }
                    catch
                    {
                        MessageBox.Show("Coudn't create path to save Map Data");
                        return false;
                    }
                }

                //Create new JObject to parse MapData into
                JObject mapData = new JObject();


                //Add mapDataClass as first children of mapIndex
                mapData.Add("map_0", map.GetJObject());

                //save mapData to File
                SaveJobjectToFile(mapData, path);
                return true;
            }
        }      

        private void LoadMapData(string imagePath)
        {
            //loads the corresponding map data to the selected image
            JObject mapsData = LoadJobjectFromFile(dataDirectory+_mapDataFileName);
            for(int i = 0; i < mapsData.Count; i++)
            {
                if ((string)mapsData[$"map_{i}"]["Path"] == imagePath)
                {
                    map.ParseJObject((JObject)mapsData[$"map_{i}"]);
                    return;
                }
            }
            //throws error when there is no matching data to the selected map
            MessageBox.Show("No Data for loaded map available");
        }

        #endregion       

        #region JSON

        private void SaveJobjectToFile(JObject data, string path)
        {
            //Saves a JObject to a file
            using (StreamWriter file = File.CreateText(path))
            using (JsonTextWriter writer = new JsonTextWriter(file))
            {
                data.WriteTo(writer);
            }
        }

        private JObject LoadJobjectFromFile(string path)
        {
            JObject data;
            //Load Data from File into JObject
            using (StreamReader file = File.OpenText(path))
            using (JsonTextReader reader = new JsonTextReader(file))
            {
                data = (JObject)JToken.ReadFrom(reader);

            }

            return data;
        }

        #endregion

        #region Webserver

        private void WebServerSetup()
        {
            Server webServer = new Server("localhost", 9000, false, DefaultRequest);

            webServer.Routes.Static.Add(WatsonWebserver.HttpMethod.GET, "/telemetry/", GetDroneTelemetrie);
            webServer.Start();
        }

        static async Task DefaultRequest(HttpContext ctx)
        {
            //Default Response for HttpRequest
            await ctx.Response.Send("BaseHub Web Server online");
        }

        async Task GetDroneTelemetrie(HttpContext ctx)
        {
            Telemetry telemetry = new Telemetry();
            try
            {
                telemetry.Name = ctx.Request.Query.Elements["name"];
                if (!comboBox_selectDorne.Items.Contains(telemetry.Name))
                {
                    UpdateDroneCombobox(comboBox_selectDorne, telemetry.Name);
                }                
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await ctx.Response.Send("Error: The name of the drone is a required key value pair");
                return;
            }
            
            telemetry.Heading = ctx.Request.Query.Elements["heading"];

            telemetry.Latitude = float.Parse(ctx.Request.Query.Elements["lat"]);
            telemetry.Longitude = float.Parse(ctx.Request.Query.Elements["long"]);
            
            telemetry.Height = int.Parse(ctx.Request.Query.Elements["height"]);
            telemetry.Velocity = int.Parse(ctx.Request.Query.Elements["velocity"]);
            telemetry.Battery = int.Parse(ctx.Request.Query.Elements["battery"]);

            System.Diagnostics.Debug.WriteLine(ctx.Request.Query.Querystring);
            await ctx.Response.Send("Drone Telemetry received successfully");

            //add Telemetry to Database
            dataBase.InsertTelemetry(telemetry);

            //update shown telemetry
            if (GetSelectedItem(comboBox_selectDorne)==telemetry.Name)
            {
                RefreshTelemetry(dataBase.SelectLatestTelemetry(telemetry.Name));
            }            
        }

        public void RefreshTelemetry(Telemetry telemetry)
        {
            UpdateControlText(comboBox_selectDorne, telemetry.Name);
            UpdateControlText(textBox_latitude, telemetry.Latitude.ToString());
            UpdateControlText(textBox_longitude, telemetry.Longitude.ToString());
            UpdateControlText(textBox_height, telemetry.Height.ToString());
            UpdateControlText(textBox_velocity, telemetry.Velocity.ToString());
            UpdateControlText(textBox_heading, telemetry.Heading);
            UpdateControlText(textBox_battery, telemetry.Battery.ToString());
        }

        public static void UpdateControlText(Control control, string newText)
        {
            control.Invoke(new Action(() => control.Text = newText));             
        }

        public static void UpdateDroneCombobox(ComboBox box, string newItem)
        {
            box.Invoke(new Action(() => box.Items.Add(newItem)));            
        }

        public static string GetSelectedItem(ComboBox box)
        {
            string selectedItem = "";
            try
            {
                box.Invoke(new MethodInvoker(delegate () { selectedItem = box.SelectedItem.ToString(); }));
                return selectedItem;
            }
            catch
            {
                return selectedItem;
            }
        }

        #endregion

        #region test

        private void DevTestBench()
        {
            /*
            textBox_location.Text = "Eilenburg";
            comboBox_mapType.Text = "hybrid";
            button_search.Enabled = true;
            button_search.PerformClick();
            button_save.PerformClick();*/

        }
        #endregion

        #region events

        private void Button_Search_Click(object sender, EventArgs e)
        {
            //Set the required values for the API Requests
            map.Query = textBox_location.Text;
            map.Width = pictureBox_map.Width;
            map.Height = pictureBox_map.Height;

            map.Zoom = trackBar_mapZoom.Value;

            string size;
            map.Scale = GetMapSize(pictureBox_map.Width, pictureBox_map.Height, 1, out size);
            map.Type = comboBox_mapType.Text;
            map.calcScale();

            //Build Google Maps Static API Request
            UriBuilder imageUri = new UriBuilder();
            imageUri.Scheme = "http";
            imageUri.Host = "maps.googleapis.com";
            imageUri.Path = "/maps/api/staticmap";
            imageUri.Query = $"center={map.Query}&size={size}&scale={map.Scale}&zoom={map.Zoom}&maptype={map.Type}&key={apiKey}";

            //Build Google Maps Geocoding API Request
            UriBuilder locationUri = new UriBuilder();
            locationUri.Scheme = "https";
            locationUri.Host = "maps.googleapis.com";
            locationUri.Path = "/maps/api/geocode/json";
            locationUri.Query = $"address={map.Query}&key={apiKey}";

            //Download the desired map from google maps and display it in picture box
            Stream imageStream;
            if(HttpGetStream(imageUri.Uri,out imageStream))
            {
                var image = System.Drawing.Image.FromStream(imageStream);
                pictureBox_map.Image = image;
            }

            //Get the corresponding coordinates from the center of the map using the same query
            if (HttpGetString(locationUri.Uri,out string data))
            {
                JObject locationData = JObject.Parse(data);
                map.Latitude = (double)locationData["results"][0]["geometry"]["location"]["lat"];
                map.Longitude = (double)locationData["results"][0]["geometry"]["location"]["lng"];
            };
            
        }

        private void trackBar_MapZoom_ValueChanged(object sender, EventArgs e)
        {
            numericUpDown_mapSize.Value = trackBar_mapZoom.Value;
        }

        private void numericUpDown_MapSize_ValueChanged(object sender, EventArgs e)
        {
            trackBar_mapZoom.Value = (int)numericUpDown_mapSize.Value;
        }
        
        private void comboBox_MapType_SelectedIndexChanged(object sender, EventArgs e)
        {
            button_search.Enabled = true;
        }

        private void textBox_location_KeyPress(object sender, KeyPressEventArgs e)
        {
            if(e.KeyChar == (char)Keys.Enter && button_search.Enabled == true)
            {
                Button_Search_Click(sender, e);
            }
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            //Open and configure a Save File Dialog to get the file path
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.AddExtension = true;

            saveFileDialog.Filter = "Images|*.png";
            saveFileDialog.FileName = map.Name;
            saveFileDialog.DefaultExt = "png";
            saveFileDialog.Title = "Save Map";

            saveFileDialog.FileName = map.Query;

            if(saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                string path = saveFileDialog.FileName;
                FileStream stream = File.Create(path);
                pictureBox_map.Image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                map.Path = stream.Name;
                map.Name = System.IO.Path.GetFileName(stream.Name);
                stream.Close();
            }

            SaveMapData(map, dataDirectory+_mapDataFileName);

        }

        private void button_load_Click(object sender, EventArgs e)
        {
            //Open and configure a Load File Dialog to get the file from a path
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;
            openFileDialog.AddExtension = true;
            openFileDialog.Multiselect = false;

            openFileDialog.Filter = "Images|*.png";
            openFileDialog.DefaultExt = "png";
            openFileDialog.Title = "Open Map";

            if(openFileDialog.ShowDialog() == DialogResult.OK)
            {                
                //Open File into FileStream from path
                FileStream fileStream = File.OpenRead(openFileDialog.FileName);

                LoadMapData(openFileDialog.FileName);

                System.Drawing.Image image = System.Drawing.Image.FromStream(fileStream);
                pictureBox_map.Image = image;
            }
        }

        private void main_Shown(object sender, EventArgs e)
        {
            //Load ini.json from Ressource Directory
            string ressourceDirectory = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName;
            LoadIni($"{ressourceDirectory}\\Resources\\ini.json");
            dataBase.CreateDatabase(dataDirectory + _telemetryDatabaseName);
            
        }

        #endregion

        private void pictureBox_map_DoubleClick(object sender, EventArgs e)
        {            
            MouseEventArgs mouse = e as MouseEventArgs;
            MessageBox.Show($"X: {mouse.X}  Y: {mouse.Y}");            
        }

        private void comboBox_selectDorne_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshTelemetry(dataBase.SelectLatestTelemetry(comboBox_selectDorne.SelectedItem.ToString()));
        }
    }
}


