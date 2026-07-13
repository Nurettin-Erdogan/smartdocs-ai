        // backend’in çalışıp çalışmadığını kontrol etmek için yazılmış en basit controller’dır.

        //ASP.NET Core’un controller ve API özelliklerini kullanabilmemizi sağlar.
        using Microsoft.AspNetCore.Mvc;      


        // Bu sınıfın Controllers grubu içerisinde bulunduğunu belirtir.
        // Namespace, sınıfları düzenli gruplamak ve isim karışıklığını önlemek için kullanılır.
        namespace SmartDocsAI.API.Controllers   
        {


        // Bu sınıfın bir Web API controller'ı olduğunu ASP.NET Core'a bildirir.
        // Gelen HTTP isteklerinin bu sınıfa yönlendirilmesini kolaylaştırır.
        [ApiController]      

        // Bu controller'ın temel adresini belirler.
        // [controller] kısmı sınıfın isminden otomatik oluşturulur.
        // HomeController isminden "Controller" çıkarılır ve adres /api/home olur.
        [Route("api/[controller]")]



        // HomeController isimli sınıfı oluşturuyoruz.
        // ControllerBase'den kalıtım aldığı için Ok(), NotFound() ve BadRequest()
        // gibi hazır HTTP cevap metotlarını kullanabilir.
        public class HomeController : ControllerBase
        {

            // Altındaki metodun GET isteklerini karşılayacağını belirtir.
            // Route ile birleştiğinde endpoint: GET /api/home olur.
            [HttpGet]

          // GET /api/home isteği geldiğinde çalışacak metottur.
          // IActionResult, metodun bir HTTP cevabı döndüreceğini belirtir.
         public IActionResult Get()
         {
             // İstemciye HTTP 200 OK cevabı gönderir.
            // 200 OK, backend’in isteği başarıyla işlediğini söyleyen HTTP durum kodudur.
            // Cevabın içinde backend'in çalıştığını belirten yazı bulunur.
            return Ok("SmartDocs AI Backend Çalışıyor!");
         }
         }
        }