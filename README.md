# DeploymentAutmate APIs

.Net Core Web Application to automate the deployment process for updating code and executing databases changes for Web Application hosted on IIS Server

## How It Works

-> Upload the latest code files that needs to be updated for the application in a zip folder  Or for Executing script upload the script in a text file using "/upload-files" endpoint

-> for Updating Code, Use "/update-codefiles" endpoint with Application Names in requestBody Like ["TestApp", "TestApp2"] to update the code files. 
   Make sure the Application path on server is provided in appsettings.json and Application is already hosted on IIS Server
   
-> for Executing Scripts, Use "execute-script" endpoint with DBNames in requestBody Like ["TestDB1", "TestDB2"] to execute the scripts. Make sure the connection string is provided in 
   appsettings.json file for this api
