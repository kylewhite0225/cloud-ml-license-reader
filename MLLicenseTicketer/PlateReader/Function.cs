using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Amazon.SQS;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime.Internal;
using S3Object = Amazon.Rekognition.Model.S3Object;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PlateReader;

public class Function
{
    IAmazonS3 S3Client { get; set; }

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function()
    {
        S3Client = new AmazonS3Client();
    }

    /// <summary>
    /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
    /// </summary>
    /// <param name="s3Client"></param>
    public Function(IAmazonS3 s3Client)
    {
        this.S3Client = s3Client;
    }
    
    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
    /// to respond to S3 notifications.
    /// </summary>
    /// <param name="evnt"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<string?> FunctionHandler(S3Event evnt, ILambdaContext context)
    {
        var s3Event = evnt.Records?[0].S3;
        if(s3Event == null)
        {
            return null;
        }

        string bucketName = s3Event.Bucket.Name;
        string objectKey = s3Event.Object.Key;

        AmazonRekognitionClient rekognition = new AmazonRekognitionClient();

        Console.WriteLine("Bucket and object key: {0}, {1}", bucketName, objectKey);

        try
        {
          
            S3Object image = new S3Object()
            {
                Bucket = bucketName,
                Name = objectKey
            };

            Image i = new Image()
            {
                S3Object = image
            };

            DetectTextRequest request = new DetectTextRequest()
            {
                Image = i
            };

            string[] detectedText = Array.Empty<string>();

            // Detect text in image, write to console for debugging.
            DetectTextResponse response = await rekognition.DetectTextAsync(request);
            Console.WriteLine("Detected text: ");

            bool cali = false;
            
            foreach (TextDetection str in response.TextDetections)
            {
                if (str.DetectedText == "California")
                {
                    cali = true;
                }

                detectedText.Append(str.DetectedText);
                Console.WriteLine(str.DetectedText);
            }

            if (!cali)
            {
                // TODO
                // PUT PLATE IMAGE IN MANUAL BUCKET

                // I think this leaves the Lambda?
                return "Exiting lambda.";
            }

            Ticket ticket = CreateTicket(detectedText);

            Console.WriteLine("Creating JSON from Ticket object");

            var jsonMessage = JsonSerializer.Serialize<Ticket>(ticket);

            var sqsClient = new AmazonSQSClient();
            sqsClient.SendMessageAsync(
                "queue url", jsonMessage).Wait();
        }
        catch (Exception e)
        {
            context.Logger.LogInformation($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
            context.Logger.LogInformation(e.Message);
            context.Logger.LogInformation(e.StackTrace);
            throw;
        }

        try
        {
            var response = await this.S3Client.GetObjectMetadataAsync(s3Event.Bucket.Name, s3Event.Object.Key);
            return response.Headers.ContentType;
        }
        catch(Exception e)
        {
            context.Logger.LogInformation($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
            context.Logger.LogInformation(e.Message);
            context.Logger.LogInformation(e.StackTrace);
            throw;
        }
    }

    private static Ticket CreateTicket(string[] detectedText)
    {
        var violationType = new Dictionary<string, int>()
        {
            {"No stop.", 300},
            {"No full stop on right.", 75},
            {"No right on red.", 125}
        };

        string[] locations =
        {
            "Main St and 116th Ave intersection, Bellevue", 
            "145th and Greenwood intersection, Shoreline", 
            "45th and Stone Way intersection, Seattle"
        };

        Random rand = new Random();

        int index = rand.Next(3);

        var violation = violationType.ElementAt(index).Key;
        var amount = violationType.ElementAt(index).Value;

        index = rand.Next(3);

        var location = locations[index];

        string date = DateTime.Now.ToLocalTime().ToLongDateString();

        // TODO 
        // Figure out how to isolate the plate number itself
        string plate = detectedText[0];

        return new Ticket()
        {
            date = date,
            location = location,
            violation = violation,
            amount = amount,
            plate = plate
        };
    }
}