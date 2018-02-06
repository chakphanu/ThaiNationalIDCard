ThaiNationalIDCard
==================

Credit:  APDU Command from Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496


ThaiNational IDCard(Smartcard) helper

To install ThaiNationalIDCard, run the following command in the Package Manager Console

PM> Install-Package ThaiNationalIDCard

Credit 3B 78 Type of thai smartcard See on wiki. https://github.com/kolry/ThaiNationalIDCard/wiki/_pages

``` CSharp
using ThaiNationalIDCard;
...
...
public void readCard()
{
            ThaiIDCard idcard = new ThaiIDCard();
            Personal personal = idcard.readAll();
            if (personal != null)
            {
                Console.WriteLine(personal.Citizenid);
                Console.WriteLine(personal.Birthday.ToString("dd/MM/yyyy"));
                Console.WriteLine(personal.Sex);
                Console.WriteLine(personal.Th_Prefix);
                Console.WriteLine(personal.Th_Firstname);
                Console.WriteLine(personal.Th_Lastname);
                Console.WriteLine(personal.En_Prefix);
                Console.WriteLine(personal.En_Firstname);
                Console.WriteLine(personal.En_Lastname);
                Console.WriteLine(personal.Issue.ToString("dd/MM/yyyy")); // วันออกบัตร
                Console.WriteLine(personal.Expire.ToString("dd/MM/yyyy")); // วันหมดอายุ

                Console.WriteLine(personal.Address);
                Console.WriteLine(personal.addrHouseNo); // บ้านเลขที่
                Console.WriteLine(personal.addrVillageNo); // หมู่ที่
                Console.WriteLine(personal.addrLane); // ซอย
                Console.WriteLine(personal.addrRoad); // ถนน
                Console.WriteLine(personal.addrTambol);
                Console.WriteLine(personal.addrAmphur);
                Console.WriteLine(personal.addrProvince);
            }
            else if (idcard.ErrorCode() > 0)
            {
                Console.WriteLine(idcard.Error());
            }
}            

```
