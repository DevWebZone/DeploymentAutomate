using DeploymentAutomate.DTO;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.Administration;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeploymentAutomate.Controllers
{
  [Route("deployment-api/[controller]")]
  [ApiController]
  public class DeploymentController : ControllerBase
  {
    private readonly IConfiguration _config;

    public DeploymentController(IConfiguration config)
    {
      _config = config;
    }
    [HttpPost("execute-script")]
    public async Task<ActionResult<IEnumerable<string>>> ExecuteScript([FromBody] ExecuteScriptDTO requestBody)
    {
      List<string> result = new List<string>();
      try
      {
        string currentDir = System.IO.Directory.GetCurrentDirectory();
        string inputPath = Path.Combine(currentDir, "wwwroot", "scriptfiles");
        string[] filePath = Directory.GetFiles(inputPath);
        string[] DBNames = requestBody.DBNames;
        foreach (string DBName in DBNames)
        {
          string connectionString = _config.GetConnectionString(DBName);
          string sqlquery = string.Empty;
          using(SqlConnection con = new SqlConnection(connectionString))
          {
            foreach(string file in filePath)
            {
              sqlquery = System.IO.File.ReadAllText(file);
              sqlquery = sqlquery.Replace("GO", ""); // to remove GO keyword in query
              sqlquery = Regex.Replace(sqlquery, @"[\/][*]{6}[\S\s]*?[*]{6}\/", ""); // to remove comments in query
              using(SqlCommand cmd = new SqlCommand())
              {
                cmd.CommandType = System.Data.CommandType.Text;
                con.Open();
                try
                {
                  await cmd.ExecuteNonQueryAsync();
                  result.Add("Query Executed Successfully on DB: " + DBName);
                }
                catch (Exception ex)
                {
                  result.Add("Error Executing query on DB: " + DBName + "Error : " + ex.Message);
                  con.Close();
                  throw;

                }
                con.Close() ;
              }
            }

          }
        }
        return Ok(result.ToArray());

      }
      catch(Exception ex)
      {
        result.Add("Error : " + ex.Message);
        return BadRequest(result.ToArray());
      }
    }
    [HttpPost("upload-files")]
    public async Task<ActionResult<string>> UploadCodeFiles()
    {
      var formCollection = await Request.ReadFormAsync();
      var file = formCollection.Files.First();
      string currentDir = System.IO.Directory.GetCurrentDirectory();
      var requestType = formCollection["RequestType"];
      string folderPath = Path.Combine(currentDir, "wwwroot");
      string fileExt = System.IO.Path.GetExtension(ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim('"'));
      if (ValidateAllowedFileExt(fileExt, requestType))
        return BadRequest("Invalid File Type Provided");
      if(file.Length > 0)
      {
        string fileName = requestType + fileExt;
        var fullPath = Path.Combine(folderPath, requestType);
        using( var stream = new FileStream(Path.Combine(folderPath, fileName), FileMode.Create))
        {
          await file.CopyToAsync(stream);
        }
        if(Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true); 
        }
        ZipFile.ExtractToDirectory(Path.Combine(folderPath, fileName), fullPath, true);

        return Ok("files Uploaded Successfully");
      }
      else
      {
        return BadRequest("File could not be uploaded");
      }
    }
    [HttpPost("update-codefiles")]
    public ActionResult<string> UpdateCode([FromBody] UpdateCodeDTO requestBody)
    {
      List<string> result = new List<string>();
      try
      {
        string currentDir = System.IO.Directory.GetCurrentDirectory();
        string inputPath = Path.Combine(currentDir, "wwwroot", "codefiles");
        string[] inputdirs = Directory.GetDirectories(inputPath, "*", SearchOption.AllDirectories);
        DirectoryInfo inputDir= new DirectoryInfo(inputPath);
        string[] instances = requestBody.Instances;
        FileInfo[] inputFiles = inputDir.GetFiles("*", SearchOption.AllDirectories);
        foreach(string instance in instances)
        {
          string basePath = _config["Instances:" + instance + ":basePath"];
          string basetempPath = Path.Combine(basePath, "temp");
          string tempdir = string.Empty;
          string tempPath = string.Empty;
          if(Directory.Exists(basetempPath))
          {
            Directory.Delete(basetempPath, true);
          }
          bool isAppStopped = StopIISWebsite(instance);
          Thread.Sleep(4000);
          if(isAppStopped)
          {
            if(inputFiles.Length > 0)
            {
              try
              {
                foreach(FileInfo file in inputFiles)
                {
                  tempdir = file.Directory.FullName.Replace(inputPath + "\\", "/");
                  tempPath = Path.Combine(basetempPath, tempdir);
                  if(!Directory.Exists(tempPath))
                  {
                    Directory.CreateDirectory(tempPath);
                  }
                  string filePath = Path.Combine(basePath, tempdir);
                  if (!Directory.Exists(filePath))
                  {
                    Directory.CreateDirectory(filePath);
                  }
                  filePath = Path.Combine(filePath, file.Name);
                  if(System.IO.File.Exists(filePath))
                  {
                    System.IO.File.Copy(filePath, Path.Combine(tempPath, file.Name), true);
                    System.IO.File.Delete(filePath);
                  }
                  System.IO.File.Copy(file.FullName, filePath, true);
                  SetGroupUsersPermission(filePath, "IIS_Users");

                }
              }
              catch (Exception ex)
              {
                RevertChanges(Path.Combine(basePath, "temp"), Path.Combine(basePath, tempdir));
              }

              Thread.Sleep(4000);
              bool StartApp = StartIISWebsite(instance);
              try
              {
                string backupPath = Path.Combine(basePath, "Backup");
                if (!Directory.Exists(backupPath))
                {
                  Directory.CreateDirectory(backupPath);
                }
                backupPath = Path.Combine(backupPath, instance + "_Backup_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".zip");
                if (System.IO.File.Exists(backupPath))
                {
                  System.IO.File.Delete(backupPath);
                }
                ZipFile.CreateFromDirectory(basetempPath, backupPath);
              }
              catch (Exception ex)
              {

                throw;
              }
            }
            else
            {
              Thread.Sleep(4000);
              bool StartApp = StartIISWebsite(instance);
            }
          }
        }
        return Ok("Code updated Successfully");
      }
      catch(Exception ex) 
      {
        return BadRequest("Unexpected error Occured" + ex.Message);
      }

    }
    private static ServerManager server = new ServerManager();
    private static bool StopIISWebsite(string instance)
    {
      var site = server.Sites.FirstOrDefault(x => x.Name == instance);
      if (site != null)
      {
        site.Stop();
        if(site.State == ObjectState.Stopped)
        {
          ApplicationPool appPool = server.ApplicationPools[site.Applications["/"].ApplicationPoolName];
          if(appPool != null)
          {
            appPool.Recycle();
            Thread.Sleep(500);
          }
          else
          {
            return false;
          }
        }
        else
        {
          return false;
        }

      }
      else
      {
        return false;
      }
      return true;
    }
    private static bool StartIISWebsite(string instance)
    {
      var site = server.Sites.FirstOrDefault(x => x.Name == instance);
      if (site != null && site.State == ObjectState.Stopped)
      {
        site.Start();
        if (site.State == ObjectState.Started)
        {
          
            return true;
          
        }
        else
        {
          return false;
        }

      }
      else
      {
        return false;
      }
    }

    private static void RevertChanges(string tempDir, string fileDir)
    {
      try
      {
        DirectoryInfo inputDir = new DirectoryInfo(tempDir);
        FileInfo[] inputFiles = inputDir.GetFiles("*", SearchOption.AllDirectories);
        foreach (FileInfo file in inputFiles)
        {
          string filePath = Path.Combine(file.Directory.FullName.Replace("\\temp", ""), file.Name);
          if (System.IO.File.Exists(filePath))
          {
            System.IO.File.Delete(filePath);
          }
          System.IO.File.Copy(file.FullName, filePath, true);

        }
      }
      catch (Exception)
      {

        throw;
      }
      

    }

    private static void SetGroupUsersPermission(string filePath, string groupName)
    {

      try
      {
        DirectoryInfo dirInfo = new DirectoryInfo(filePath);
        DirectorySecurity dirSec  = dirInfo.GetAccessControl();
        string strUsers = "";
        if(groupName.Equals("Users"))
        {
          var sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
          strUsers = sid.Translate(typeof(NTAccount)).ToString();
        }
        else
        {
          strUsers = "BUILTIN\\IIS_IUSRS";
        }
        var fsAccessRule = new FileSystemAccessRule(strUsers, FileSystemRights.Write, AccessControlType.Allow);
        dirSec.ModifyAccessRule(AccessControlModification.Add, fsAccessRule, out bool modified);
        dirInfo.SetAccessControl(dirSec);
        return;
      }
      catch (Exception)
      {
        throw;
      }
    }

    private static bool ValidateAllowedFileExt(string fileExt, string RequestType)
    {
      string allowedFileExts = string.Empty;
      if (RequestType.Contains("scriptfiles"))
        allowedFileExts = ".txt";
      else if (RequestType.Contains("codefiles"))
        allowedFileExts = ".zip";
      if (allowedFileExts.Contains(fileExt.ToLower()))
        return true;
      return false;
    }
  }
}
