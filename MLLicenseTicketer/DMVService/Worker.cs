using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Amazon.SQS;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;
using Amazon.Runtime.CredentialManagement;
using Amazon.Runtime;
using Amazon.SQS.Model;

namespace DMVService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private const string logPath = @"C:\Temp\DMVData.log";

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            //Do here anything you want to do when the service starts

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            //Do here anything you want to do when the service stops

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                WriteToLog("\n\nBeginning...");
                AmazonSQSClient sqsClient;
                try
                {
                    sqsClient = new AmazonSQSClient(GetAwsCredentials(), Amazon.RegionEndpoint.USEast1);

                    WriteToLog("Receiving SQS Message from downward queue...");
                    var response = await sqsClient.ReceiveMessageAsync(
                        "https://sqs.us-east-1.amazonaws.com/758232842797/downward-queue"
                        );
                    WriteToLog("Received message.");

                    string dbPath = @"C:\Users\Kylew\Dropbox\CS455 - Cloud Computing\Projects\Project 3\cloud-ml-license-reader\DMVDatabase.xml";
                    XDocument dbDoc = XDocument.Load(dbPath);
                    WriteToLog("Loaded XML DMV Database");

                    foreach (var message in response.Messages)
                    {
                        WriteToLog("message: " + message.Body);
                        Ticket? ticket = JsonSerializer.Deserialize<Ticket>(message.Body);
                        if (ticket == null)
                        {
                            throw new Exception("Could not deserialize queue message into Ticket object.");
                        }
                        WriteToLog("Deserialized Ticket information successfully.");

                        var element = dbDoc.Descendants()
                            .Where(x => (string)x.Attribute("plate") == ticket.plate)
                            .FirstOrDefault();
                        WriteToLog("Attemping to find vehicle in database...");

                        Vehicle vehicle = new Vehicle();
                        vehicle.plate = ticket.plate;
                        if (element != null)
                        {
                            WriteToLog("Found vehicle in database. Accessing owner information.");

                            // Found plate number, populate vehicle related fields using XPath
                            var make = element.XPathSelectElement("./make").Value;
                            vehicle.make = make;

                            var model = element.XPathSelectElement("./model").Value;
                            vehicle.model = model;

                            var color = element.XPathSelectElement("./color").Value;
                            vehicle.color = color;

                            var ownerElement = element.XPathSelectElement("./owner");
                            var preferredLanguage = ownerElement.Attribute("preferredLanguage").Value;
                            vehicle.preferredLanguage = preferredLanguage;

                            var name = ownerElement.XPathSelectElement("./name").Value;
                            vehicle.name = name;

                            var contact = ownerElement.XPathSelectElement("./contact").Value;
                            vehicle.contact = contact;
                        }

                        // Populate ticket specific information in vehicle object
                        vehicle.violationType = ticket.violation;
                        vehicle.violationLocation = ticket.location;
                        vehicle.date = ticket.date;
                        vehicle.ticketAmount = ticket.amount;

                        WriteToLog("Deleting message from queue...");
                        await sqsClient.DeleteMessageAsync("https://sqs.us-east-1.amazonaws.com/758232842797/downward-queue", message.ReceiptHandle);

                        // Send vehicle info to upward queue
                        WriteToLog("Sending message to queue...");
                        string jsonMessage = JsonSerializer.Serialize<Vehicle>(vehicle);
                        await sqsClient.SendMessageAsync(
                            "https://sqs.us-east-1.amazonaws.com/926831757693/upwardQueue",
                            jsonMessage);
                        WriteToLog(jsonMessage);
                        WriteToLog("Successfully sent message.");
                    }
                }
                catch (Exception e)
                {
                    WriteToLog(e.Message);
                    throw e;
                }

                await Task.Delay(5000, stoppingToken);
            }
        }

        public void WriteToLog(string message)
        {
            string text = String.Format("{0}:\t{1}", DateTime.Now, message);
            using (StreamWriter writer = new StreamWriter(logPath, append: true))
            {
                writer.WriteLine(text);
            }
        }

        private static SessionAWSCredentials GetAwsCredentials()
        {
            return new SessionAWSCredentials(
                // TODO
                // Update session credentials
                "",
                "",
                "");
        }
    }
}
