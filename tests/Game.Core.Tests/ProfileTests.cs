using Game.Core;

namespace Game.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void RewardsDoNotDuplicateAfterSavingAndReloading()
    {
        var catalog=DomainTests.Catalog();var profile=new PlayerProfile();
        var reward=new RewardReceipt(Guid.NewGuid(),RewardKind.Campaign,90,new("Wygrana",PlayerLoadoutDto.Default,Guid.NewGuid(),DateTimeOffset.UtcNow));
        Assert.True(ProfileRules.ApplyReward(catalog,profile,reward));
        profile=ProfileCodec.Decode(catalog,ProfileCodec.Encode(catalog,profile));
        Assert.False(ProfileRules.ApplyReward(catalog,profile,reward));Assert.Equal(90,profile.Coins);
        ProfileRules.Archive(profile,2);Assert.Equal("Wygrana",profile.Presets[2].Name);
        Assert.True(ProfileRules.BuyCosmetic(catalog,profile,"ember"));Assert.Equal(30,profile.Coins);
        Assert.False(ProfileRules.BuyCosmetic(catalog,profile,"ember"));
    }
    [Fact]
    public void CorruptionIsDetectedAndAtomicStoreRecoversBackup()
    {
        var catalog=DomainTests.Catalog();string root=Path.Combine(Path.GetTempPath(),"mcm-profile-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"profile.json");var repo=new ProfileRepository(catalog,file);var profile=new PlayerProfile();
            repo.Save(profile);profile.Coins=100;repo.Save(profile);
            File.WriteAllText(file,"broken");var recovered=repo.Load(out string warning);
            Assert.Contains("kopii",warning);Assert.Equal(0,recovered.Coins);Assert.True(Directory.GetFiles(root,"*.corrupt-*").Length==1);
            repo.Save(recovered);Assert.NotEmpty(File.ReadAllText(file));Assert.Empty(Directory.GetFiles(root,"*.tmp-*"));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void UnknownSchemaIsPreservedAndChecksumsRejectTampering()
    {
        var catalog=DomainTests.Catalog();var profile=new PlayerProfile();var json=ProfileCodec.Encode(catalog,profile);
        Assert.Throws<InvalidDataException>(()=>ProfileCodec.Decode(catalog,json.Replace("\"coins\":0","\"coins\":999")));
        Assert.Throws<NotSupportedException>(()=>ProfileCodec.Decode(catalog,json.Replace("\"schemaVersion\":1","\"schemaVersion\":99")));
        Assert.Equal(3,profile.Presets.Length);
    }
}
