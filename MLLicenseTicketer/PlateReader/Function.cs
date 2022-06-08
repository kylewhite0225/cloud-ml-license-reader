using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using Amazon.Runtime.Internal;
using S3Object = Amazon.Textract.Model.S3Object;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text.Json;
using System.Linq;
using System.Text.RegularExpressions;

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

        // AmazonRekognitionClient rekognition = new AmazonRekognitionClient();
        AmazonTextractClient textract = new AmazonTextractClient();

        Console.WriteLine("Bucket and object key: {0}, {1}", bucketName, objectKey);

        try
        {
            S3Object image = new Amazon.Textract.Model.S3Object()
            {
                Bucket = bucketName,
                Name = objectKey
            };


            Document doc = new Document()
            {
                S3Object = image
            };

            DetectDocumentTextRequest request = new DetectDocumentTextRequest()
            {
                Document = doc
            };

            Console.WriteLine("Detected text: ");

            DetectDocumentTextResponse response = await textract.DetectDocumentTextAsync(request);

            List<string> detectedText = new List<string>();
            string plateNumber = "";

            bool cali = false;
            bool plate = false;

            foreach (Block b in response.Blocks)
            {
                if (b.Text == null)
                {
                    continue;
                }

                if (b.Text == "California")
                {
                    cali = true;
                }

                plate = PlateDetector(b.Text);

                if (plate)
                {
                    plateNumber = b.Text;
                }

                if (cali && PlateDetector(plateNumber))
                {
                    break;
                }

                // detectedText.Add(b.Text);
                Console.WriteLine(b.Text);
            }

            if (!cali)
            {
                // Put plate image into manual sorting bucket.
                MoveFile(bucketName, objectKey, "manual-plate-bucket", objectKey).Wait();

                // I think this leaves the Lambda?
                return "Exiting lambda.";
            } else if (!PlateDetector(plateNumber))
            {
                throw new Exception("Valid plate number not found.");
            }

            var metadataRequest = await this.S3Client.GetObjectMetadataAsync(s3Event.Bucket.Name, s3Event.Object.Key);

            MetadataCollection metadata = metadataRequest.Metadata;

            Ticket ticket = CreateTicket(plateNumber, metadata);

            Console.WriteLine("Creating JSON from Ticket object");

            var jsonMessage = JsonSerializer.Serialize<Ticket>(ticket);

            var sqsClient = new AmazonSQSClient();
            sqsClient.SendMessageAsync(
                "https://sqs.us-east-1.amazonaws.com/926831757693/downwardQueue", 
                jsonMessage).Wait();
        }
        catch (Exception e)
        {
            // context.Logger.LogInformation($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
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

    private static Ticket CreateTicket(string plate, MetadataCollection metadata)
    {
        var violationAmount = new Dictionary<string, int>()
        {
            {"No stop.", 300},
            {"No full stop on right.", 75},
            {"No right on red.", 125}
        };

        var amount = violationAmount[metadata["violation"]];

        return new Ticket()
        {
            date = metadata["date"],
            location = metadata["location"],
            violation = metadata["violation"],
            amount = amount,
            plate = plate
        };
    }

    private static bool PlateDetector(string plate)
    {
        if (plate == null)
        {
            return false;
        }
        
        if (plate.Length == 7 && Regex.IsMatch(plate, "^(?=.*[0-9])(?=.*[a-zA-Z])([a-zA-Z0-9]+)$"))
        {
            return true;
        }

        return false;
    }

    private static async Task MoveFile(string source, string sourceKey, string destination, string destKey)
    {
        AmazonS3Client s3Client = new AmazonS3Client();

        try
        {
            CopyObjectRequest copy = new CopyObjectRequest()
            {
                SourceBucket = source,
                SourceKey = sourceKey,
                DestinationBucket = destination,
                DestinationKey = destKey
            };

            CopyObjectResponse response = await s3Client.CopyObjectAsync(copy);
            Console.WriteLine("File copy completed.");

            s3Client.Dispose();
            // return Task.CompletedTask;
        }
        catch (AmazonS3Exception e)
        {
            if (e.ErrorCode != null &&
                (e.ErrorCode.Equals("InvalidAccessKeyId")
                 ||
                 e.ErrorCode.Equals("InvalidSecurity")))
            {
                throw new Exception("Check the provided AWS Credentials.");
            }
            else
            {
                throw new Exception("Error occurred: " + e.Message);
            }
        }
    }
}