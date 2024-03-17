namespace DeploymentAutomate.DTO
{
  public class ExecuteScriptDTO
  {
    public string[] DBNames { get; set; }
  }
  
  public class UpdateCodeDTO
  {
    public string[] Instances { get; set; }
  }
}
